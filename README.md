# CppWinRT.Builders

Add-on NuGet package to generate C++ builder-style helpers for WinRT objects.

### Motivation
In C++/WinRT, setters are implemented as method calls that return `void`. 
This means that creating a button from code looks like:

```cpp
  auto button = Controls::Button();
  button.Height(40);
  button.Width(200);
  button.Content(winrt::box_value(L"Hello"));
```

This can get repetitive, and some developers prefer a more fluent style.
*CppWinRT.Builders* enables you to write this instead:

```cpp
#include <winrt/builders/Windows.UI.Xaml.Controls.Button.h>
  auto button = Controls::builders::Button()
    .Height(40)
    .Width(200)
    .Content(winrt::box_value(L"Hello"));
```

Collections (such as types that derive from `IVector<T>` and `IMap<T>`) can also be initialized via C++ initializer lists:
```cpp
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
``` 
