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
