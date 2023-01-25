#include "pch.h"
#include "MainPage.h"
#include "MainPage.g.cpp"
#include <winrt/builders/Windows.UI.Xaml.Controls.Button.h>
#include <winrt/builders/Windows.UI.Xaml.Controls.StackPanel.h>
#include <winrt/builders/Windows.UI.Xaml.Application.h>
#include <winrt/builders/RuntimeComponent1.Class.h>
#include <winrt/builders/helpers.h>

#include <winrt/Windows.UI.Xaml.Media.h>
#include <type_traits>
using namespace winrt;
using namespace Windows::UI::Xaml;


template<typename R, typename T, typename... Args>
struct FunctionTraitsBase {
  using RetType = R;
  using Type = T;
  using ArgTypes = std::tuple<Args...>;
  static constexpr std::size_t ArgCount = sizeof...(Args);
  template<std::size_t N>
  using NthArg = std::tuple_element_t<N, ArgTypes>;
};

template<typename F> struct FunctionTraits;

template<typename T, typename R, typename... Arg>
struct FunctionTraits<R(__cdecl T::*)(Arg...) const>
  : FunctionTraitsBase<R, T, Arg...> {};

struct UIElementCollection {
  void Append(UIElement const&) const;
};

template<typename T>
using element_type_IVector_cvref = typename FunctionTraits<decltype(&T::Append)>::template NthArg<0>;

template<typename T>
using element_type_IVector = typename std::remove_cv_t<typename std::remove_reference_t<typename element_type_IVector_cvref<T>>>;

template<typename T>
using key_type_IMap_cvref = typename FunctionTraits<decltype(&T::Insert)>::template NthArg<0>;

template<typename T>
using value_type_IMap_cvref = typename FunctionTraits<decltype(&T::Insert)>::template NthArg<1>;

template<typename T>
using key_type_IMap = typename std::remove_cv_t<typename std::remove_reference_t<typename key_type_IMap_cvref<T>>>;

template<typename T>
using value_type_IMap = typename std::remove_cv_t<typename std::remove_reference_t<typename value_type_IMap_cvref<T>>>;

template<typename T>
std::enable_if_t<std::is_assignable_v<winrt::Windows::Foundation::Collections::IMap<key_type_IMap<T>, value_type_IMap<T>>, T>, T> 
Build(std::initializer_list<std::pair<key_type_IMap<T>, value_type_IMap<T>>> const& values) {
  T map{};
  for (const auto& v : values) {
    map.Insert(v.first, v.second);
  }
  return map;
}

template<typename T, typename U>
std::enable_if_t<
  std::is_assignable_v<winrt::Windows::Foundation::Collections::IVector<element_type_IVector<T>>, T> && 
  std::is_assignable_v<U, element_type_IVector<T>>, T>
Build(std::initializer_list<U> const& values) {
  T vector{};
  for (const auto& v : values) {
    vector.Append(v);
  }
  return vector;
}


namespace winrt::WinRTBuilderSample::implementation
{


    MainPage::MainPage()
    {
        InitializeComponent();

        auto c = winrt::RuntimeComponent1::builders::Class()
          .MyProperty(42)
          .StringVector({ L"foo", L"bar" })
          .StringMapVector({ winrt::builders::make_map<hstring>({ {L"a", L"b"}}),
            winrt::builders::make_map<hstring, hstring>({ {L"a", L"b"}}),
            })
          ;

        // C++/WinRT object creation, property setters, and children
        auto boringPanel = Controls::StackPanel();
        boringPanel.Width(400);
        boringPanel.Height(40);
        auto boringButton = Controls::Button();
        boringButton.Content(winrt::box_value(L"'Sup"));

        // CppWinRT.Builders style!
        auto coolPanel = Controls::builders::StackPanel()
          .Width(400)
          .Height(40)
          .Children({
              Controls::builders::Button()
              .Content(winrt::box_value(L"'Sup!"))
            });
        
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
