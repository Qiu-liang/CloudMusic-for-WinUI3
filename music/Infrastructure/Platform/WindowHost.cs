using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace music.Infrastructure.Platform
{
    internal sealed class WindowHost
    {
        public Window? Window { get; private set; }
        public Frame? ContentFrame { get; private set; }
        public Action<string>? ApplyThemeCallback { get; private set; }

        public XamlRoot? XamlRoot => ContentFrame?.XamlRoot ?? Window?.Content?.XamlRoot;

        public void Attach(Window window, Frame contentFrame, Action<string> applyThemeCallback)
        {
            Window = window;
            ContentFrame = contentFrame;
            ApplyThemeCallback = applyThemeCallback;
        }

        public void Detach()
        {
            Window = null;
            ContentFrame = null;
            ApplyThemeCallback = null;
        }
    }
}
