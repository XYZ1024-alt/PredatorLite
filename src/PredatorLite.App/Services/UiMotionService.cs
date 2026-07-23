using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace PredatorLite.App.Services;

public sealed class UiMotionService : IDisposable
{
    private const int PageDurationMilliseconds = 160;
    private const int ShellDurationMilliseconds = 180;
    private const int OsdEnterDurationMilliseconds = 180;
    private const int OsdExitDurationMilliseconds = 120;
    private const int PressDurationMilliseconds = 100;
    private const int ReleaseDurationMilliseconds = 140;
    private const float PressedScale = 0.985f;
    private const float TranslationDistance = 8f;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly UISettings _uiSettings = new();
    private readonly ConditionalWeakTable<UIElement, AnimationState> _animationStates = new();
    private readonly List<WeakReference<UIElement>> _trackedElements = [];
    private readonly Dictionary<uint, FrameworkElement> _pressedElements = [];
    private bool _disposed;

    public UiMotionService(DispatcherQueue dispatcherQueue)
    {
        _dispatcherQueue = dispatcherQueue;
        AnimationsEnabled = _uiSettings.AnimationsEnabled;
        _uiSettings.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
    }

    public bool AnimationsEnabled { get; private set; }

    public void AttachPressFeedback(FrameworkElement root)
    {
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
        root.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        root.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler(OnPointerReleased), true);
        root.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerReleased), true);
    }

    public Task AnimatePageInAsync(UIElement element, int direction, bool animate)
    {
        if (!animate || !AnimationsEnabled)
        {
            Settle(element, 1f, Vector3.Zero, Vector3.One);
            return Task.CompletedTask;
        }

        float offset = direction < 0 ? -TranslationDistance : TranslationDistance;
        return AnimateAsync(
            element,
            new AnimationSpec(
                0f,
                1f,
                new Vector3(offset, 0f, 0f),
                Vector3.Zero,
                PageDurationMilliseconds,
                InitializeStart: true));
    }

    public Task AnimateShellInAsync(UIElement element)
    {
        if (!AnimationsEnabled)
        {
            Settle(element, 1f, Vector3.Zero, Vector3.One);
            return Task.CompletedTask;
        }

        return AnimateAsync(
            element,
            new AnimationSpec(
                0f,
                1f,
                Vector3.Zero,
                Vector3.Zero,
                ShellDurationMilliseconds,
                InitializeStart: true));
    }

    public Task AnimateOsdInAsync(UIElement element)
    {
        if (!AnimationsEnabled)
        {
            Settle(element, 1f, Vector3.Zero, Vector3.One);
            return Task.CompletedTask;
        }

        return AnimateAsync(
            element,
            new AnimationSpec(
                0f,
                1f,
                new Vector3(0f, -TranslationDistance, 0f),
                Vector3.Zero,
                OsdEnterDurationMilliseconds,
                InitializeStart: true));
    }

    public Task AnimateOsdOutAsync(UIElement element)
    {
        if (!AnimationsEnabled)
        {
            Settle(element, 0f, Vector3.Zero, Vector3.One);
            return Task.CompletedTask;
        }

        return AnimateAsync(
            element,
            new AnimationSpec(
                1f,
                0f,
                Vector3.Zero,
                new Vector3(0f, -TranslationDistance, 0f),
                OsdExitDurationMilliseconds,
                InitializeStart: false));
    }

    public void Reset(UIElement element) => Settle(element, 1f, Vector3.Zero, Vector3.One);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _uiSettings.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
        _pressedElements.Clear();
    }

    private async Task AnimateAsync(UIElement element, AnimationSpec spec)
    {
        Track(element);
        AnimationState state = _animationStates.GetOrCreateValue(element);
        int generation = ++state.Generation;

        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        if (spec.InitializeStart)
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation("Translation");
            visual.Opacity = spec.FromOpacity;
            visual.Properties.InsertVector3("Translation", spec.FromTranslation);
        }

        CubicBezierEasingFunction easing = visual.Compositor.CreateCubicBezierEasingFunction(
            new Vector2(0.23f, 1f),
            new Vector2(0.32f, 1f));
        ScalarKeyFrameAnimation opacity = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacity.Duration = TimeSpan.FromMilliseconds(spec.DurationMilliseconds);
        opacity.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        opacity.InsertKeyFrame(1f, spec.ToOpacity, easing);

        Vector3KeyFrameAnimation translation = visual.Compositor.CreateVector3KeyFrameAnimation();
        translation.Duration = opacity.Duration;
        translation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        translation.InsertKeyFrame(1f, spec.ToTranslation, easing);

        CompositionScopedBatch batch = visual.Compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
        visual.StartAnimation(nameof(Visual.Opacity), opacity);
        visual.StartAnimation("Translation", translation);
        batch.End();
        await AwaitBatchAsync(batch);

        if (state.Generation != generation)
        {
            return;
        }

        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.Opacity = spec.ToOpacity;
        visual.Properties.InsertVector3("Translation", spec.ToTranslation);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!AnimationsEnabled || sender is not FrameworkElement root)
        {
            return;
        }

        FrameworkElement? pressable = FindPressable(e.OriginalSource as DependencyObject, root);
        if (pressable is null)
        {
            return;
        }

        uint pointerId = e.Pointer.PointerId;
        _pressedElements[pointerId] = pressable;
        AnimateScale(pressable, PressedScale, PressDurationMilliseconds);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        uint pointerId = e.Pointer.PointerId;
        if (!_pressedElements.Remove(pointerId, out FrameworkElement? pressable))
        {
            return;
        }

        AnimateScale(pressable, 1f, ReleaseDurationMilliseconds);
    }

    private void AnimateScale(FrameworkElement element, float target, int durationMilliseconds)
    {
        Track(element);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3(
            (float)Math.Max(0d, element.ActualWidth / 2d),
            (float)Math.Max(0d, element.ActualHeight / 2d),
            0f);

        if (!AnimationsEnabled)
        {
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Scale = Vector3.One;
            return;
        }

        Vector3KeyFrameAnimation animation = visual.Compositor.CreateVector3KeyFrameAnimation();
        animation.Duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        animation.StopBehavior = AnimationStopBehavior.LeaveCurrentValue;
        animation.InsertKeyFrame(
            1f,
            new Vector3(target),
            visual.Compositor.CreateCubicBezierEasingFunction(
                new Vector2(0.23f, 1f),
                new Vector2(0.32f, 1f)));
        visual.StartAnimation(nameof(Visual.Scale), animation);
    }

    private void OnAnimationsEnabledChanged(UISettings sender, object args)
    {
        bool enabled = sender.AnimationsEnabled;
        _dispatcherQueue.TryEnqueue(() =>
        {
            AnimationsEnabled = enabled;
            if (!enabled)
            {
                ResetTrackedElements();
            }
        });
    }

    private void ResetTrackedElements()
    {
        _pressedElements.Clear();
        for (int index = _trackedElements.Count - 1; index >= 0; index--)
        {
            if (!_trackedElements[index].TryGetTarget(out UIElement? element))
            {
                _trackedElements.RemoveAt(index);
                continue;
            }

            Settle(element, 1f, Vector3.Zero, Vector3.One);
        }
    }

    private void Settle(UIElement element, float opacity, Vector3 translation, Vector3 scale)
    {
        Track(element);
        AnimationState state = _animationStates.GetOrCreateValue(element);
        state.Generation++;
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        Visual visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.StopAnimation("Translation");
        visual.StopAnimation(nameof(Visual.Scale));
        visual.Opacity = opacity;
        visual.Properties.InsertVector3("Translation", translation);
        visual.Scale = scale;
    }

    private void Track(UIElement element)
    {
        for (int index = _trackedElements.Count - 1; index >= 0; index--)
        {
            if (!_trackedElements[index].TryGetTarget(out UIElement? existing))
            {
                _trackedElements.RemoveAt(index);
            }
            else if (ReferenceEquals(existing, element))
            {
                return;
            }
        }

        _trackedElements.Add(new WeakReference<UIElement>(element));
    }

    private static FrameworkElement? FindPressable(DependencyObject? source, FrameworkElement root)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase { IsEnabled: true } button)
            {
                return button;
            }

            if (ReferenceEquals(current, root))
            {
                break;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static Task AwaitBatchAsync(CompositionScopedBatch batch)
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TypedEventHandler<object, CompositionBatchCompletedEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            batch.Completed -= handler;
            completion.TrySetResult(true);
        };
        batch.Completed += handler;
        return completion.Task;
    }

    private sealed class AnimationState
    {
        public int Generation { get; set; }
    }

    private readonly record struct AnimationSpec(
        float FromOpacity,
        float ToOpacity,
        Vector3 FromTranslation,
        Vector3 ToTranslation,
        int DurationMilliseconds,
        bool InitializeStart);
}
