#include "pch.h"
#include "MainPage.h"
#include "MainPage.g.cpp"
#include <winrt/builders/Windows.UI.Xaml.Controls.Button.h>

using namespace winrt;
using namespace Windows::UI::Xaml;

namespace winrt::WinRTBuilderSample::implementation
{
    MainPage::MainPage()
    {
        InitializeComponent();

        auto button = Controls::builders::Button()
          .Height(40)
          .Width(200)
          .Content(winrt::box_value(L"Hello"));

        panel().Children().Append(button);
    }

    int32_t MainPage::MyProperty()
    {
        throw hresult_not_implemented();
    }

    void MainPage::MyProperty(int32_t /* value */)
    {
        throw hresult_not_implemented();
    }

    void MainPage::ClickHandler(IInspectable const&, RoutedEventArgs const&)
    {
        myButton().Content(box_value(L"Clicked"));
    }
}
