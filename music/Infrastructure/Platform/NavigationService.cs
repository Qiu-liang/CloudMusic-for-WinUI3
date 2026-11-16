using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using music.Core.Abstractions;

namespace music.Infrastructure.Platform
{
    public sealed class NavigationService : INavigationService, IPageRequestFactory
    {
        private readonly WindowHost _host;
        private readonly Dictionary<PageKey, (Type PageType, Func<object>? ViewModelFactory)> _routes = new();
        private readonly Dictionary<Type, PageKey> _reverseRoutes = new();
        private Frame? _subscribedFrame;

        public event EventHandler<PageKey?>? Navigated;

        internal NavigationService(WindowHost host)
        {
            _host = host;
        }

        public void Register(PageKey key, Type pageType, Func<object>? viewModelFactory = null)
        {
            _routes[key] = (pageType, viewModelFactory);
            _reverseRoutes[pageType] = key;
        }

        public void AttachFrame(Frame frame)
        {
            if (_subscribedFrame != null)
            {
                _subscribedFrame.Navigated -= OnFrameNavigated;
            }
            _subscribedFrame = frame;
            _subscribedFrame.Navigated += OnFrameNavigated;
        }

        public void DetachFrame()
        {
            if (_subscribedFrame != null)
            {
                _subscribedFrame.Navigated -= OnFrameNavigated;
                _subscribedFrame = null;
            }
        }

        public PageKey? CurrentPage
        {
            get
            {
                var type = _host.ContentFrame?.CurrentSourcePageType;
                return type != null && _reverseRoutes.TryGetValue(type, out var key) ? key : null;
            }
        }

        public bool CanGoBack => _host.ContentFrame?.CanGoBack == true;

        public NavigationRequest Create(PageKey key, object? parameter = null)
        {
            if (_routes.TryGetValue(key, out var route) && route.ViewModelFactory != null)
                return new NavigationRequest(parameter, route.ViewModelFactory);
            throw new InvalidOperationException($"Route {key} does not have a registered ViewModel factory.");
        }

        public void Navigate(PageKey key, object? parameter = null)
        {
            var frame = _host.ContentFrame;
            if (frame == null || !_routes.TryGetValue(key, out var route)) return;

            var navParam = route.ViewModelFactory != null
                ? new NavigationRequest(parameter, route.ViewModelFactory)
                : parameter;
            frame.Navigate(route.PageType, navParam);
        }

        public void GoBack()
        {
            if (_host.ContentFrame?.CanGoBack == true)
            {
                _host.ContentFrame.GoBack();
            }
        }

        public void ClearBackStack()
        {
            var frame = _host.ContentFrame;
            if (frame == null) return;
            while (frame.CanGoBack)
            {
                frame.GoBack();
            }
        }

        public void RefreshIfCurrent(params PageKey[] pages)
        {
            var current = CurrentPage;
            if (current == null) return;
            foreach (var page in pages)
            {
                if (page == current)
                {
                    Navigate(current.Value);
                    return;
                }
            }
        }

        private void OnFrameNavigated(object sender, NavigationEventArgs e)
        {
            Navigated?.Invoke(this, _reverseRoutes.TryGetValue(e.SourcePageType, out var key) ? key : null);
        }
    }
}
