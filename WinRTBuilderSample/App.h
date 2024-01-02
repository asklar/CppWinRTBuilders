#pragma once
#include "App.xaml.g.h"

namespace winrt::WinRTBuilderSample::implementation
{
    struct App : AppT<App>
    {
        App();

        void OnLaunched(Windows::ApplicationModel::Activation::LaunchActivatedEventArgs const&);
        void OnSuspending(IInspectable const&, Windows::ApplicationModel::SuspendingEventArgs const&);
        void OnNavigationFailed(IInspectable const&, Windows::UI::Xaml::Navigation::NavigationFailedEventArgs const&);
        void OnActivated(Windows::ApplicationModel::Activation::IActivatedEventArgs const&);

        inline static winrt::event<winrt::Windows::Foundation::TypedEventHandler<winrt::hstring, winrt::hstring>> OAuthCodeReceived;
    };
}
