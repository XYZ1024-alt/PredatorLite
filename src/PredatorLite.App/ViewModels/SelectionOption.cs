namespace PredatorLite.App.ViewModels;

public abstract record SelectionOptionBase(string Name, string AutomationId);

public sealed record SelectionOption<T>(T Value, string Name, string AutomationId = "")
    : SelectionOptionBase(Name, AutomationId);
