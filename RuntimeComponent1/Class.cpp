#include "pch.h"
#include "Class.h"
#include "Class.g.cpp"

namespace winrt::RuntimeComponent1::implementation
{
    int32_t Class::MyProperty()
    {
        throw hresult_not_implemented();
    }

    void Class::MyProperty(int32_t /* value */)
    {
        throw hresult_not_implemented();
    }

    winrt::event_token Class::IntEvent(winrt::Windows::Foundation::EventHandler<int32_t> const& handler)
    {
      throw hresult_not_implemented();
    }
    void Class::IntEvent(winrt::event_token const& token) noexcept
    {
      throw hresult_not_implemented();
    }
    winrt::event_token Class::TypedEvent(winrt::Windows::Foundation::TypedEventHandler<winrt::RuntimeComponent1::Class, float> const& handler)
    {
      throw hresult_not_implemented();
    }
    void Class::TypedEvent(winrt::event_token const& token) noexcept
    {
      throw hresult_not_implemented();
    }
}
