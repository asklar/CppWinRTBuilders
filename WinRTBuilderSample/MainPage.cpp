#include "pch.h"
#include "MainPage.h"
#include "MainPage.g.cpp"
#include <winrt/builders/Windows.UI.Xaml.Controls.Button.h>
#include <winrt/builders/Windows.UI.Xaml.Controls.StackPanel.h>
#include <winrt/builders/Windows.UI.Xaml.Application.h>
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
std::enable_if_t<std::is_assignable_v<winrt::Windows::Foundation::Collections::IMap<key_type_IMap<T>, value_type_IMap<T>>, T>, T> Build(std::initializer_list<std::pair<key_type_IMap<T>, value_type_IMap<T>>> const& values) {
  T map{};
  for (const auto& v : values) {
    map.Insert(v.first, v.second);
  }
  return map;
}

template<typename T>
std::enable_if_t<std::is_assignable_v<winrt::Windows::Foundation::Collections::IVector<element_type_IVector<T>>, T>, T>  Build(std::initializer_list<element_type_IVector<T>> const& values) {
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


        auto sp = Controls::builders::StackPanel{}
          .Children({
            Controls::builders::Button{}
              .Height(40)
              .Width(200)
              .Content(winrt::box_value(L"Hello")),
            Controls::builders::Button{}
              .Height(40)
              .Width(200)
              .Content(winrt::box_value(L"world")),
            })
          .Resources({
            { winrt::box_value(L"SomeKey"), winrt::box_value(42) }
            })
          .Background(Media::SolidColorBrush{ Windows::UI::Colors::AliceBlue() })
          .Padding(ThicknessHelper::FromUniformLength(8))
          .Orientation(Controls::Orientation::Horizontal);
        

        using uiElement = element_type_IVector<Controls::UIElementCollection>;
        static_assert(std::is_same_v<uiElement, UIElement>);
        
        using ii1 = key_type_IMap<ResourceDictionary>;
        using ii2 = value_type_IMap<ResourceDictionary>;
        
        //auto rd = Build<ResourceDictionary>({
        //  { winrt::box_value(L"123"), nullptr },
        //  });


        //auto col = Build<Controls::UIElementCollection>({ button });


        //auto a1 = builders::Application()
        //  .Resources({
        //    { winrt::box_value(L"123"), nullptr },
        //    }
        //);

        //auto a2 = builders::Application()
        //  .Resources(Build<ResourceDictionary, IInspectable, IInspectable>({
        //    { winrt::box_value(L"123"), nullptr },
        //    } ));


        panel().Children().Append(sp);
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
