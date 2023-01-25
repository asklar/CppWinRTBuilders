#pragma once

#include "Class.g.h"

namespace winrt::RuntimeComponent1::implementation
{
    struct Class : ClassT<Class>
    {
        Class() = default;

        int32_t MyProperty();
        void MyProperty(int32_t value);

        winrt::Windows::Foundation::Collections::IVector<winrt::hstring> StringVector() { return nullptr; }
        winrt::Windows::Foundation::Collections::IVector<winrt::Windows::Foundation::Collections::IMap<winrt::hstring, winrt::hstring>> StringMapVector() { return nullptr; }
    };
}

namespace winrt::RuntimeComponent1::factory_implementation
{
    struct Class : ClassT<Class, implementation::Class>
    {
    };
}
