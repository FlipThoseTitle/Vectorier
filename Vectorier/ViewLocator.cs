using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Vectorier.ViewModels;

namespace Vectorier;

/// <summary>
/// Maps view models to views. The mapping is explicit (no reflection) so it keeps working when the
/// browser build is trimmed.
/// </summary>
public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Func<Control>> Factories = new();

    public static void Register<TViewModel>(Func<Control> factory) => Factories[typeof(TViewModel)] = factory;

    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        for (var type = param.GetType(); type != null; type = type.BaseType)
        {
            if (Factories.TryGetValue(type, out var factory))
                return factory();
        }

        return new TextBlock { Text = "No view registered for " + param.GetType().Name };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
