namespace PredatorLite.App.ViewModels;

public sealed record SelectionOption<T>(T Value, string Name, string AutomationId = "");
