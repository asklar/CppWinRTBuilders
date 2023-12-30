#include "pch.h"
#include "MainPage.h"
#include "MainPage.g.cpp"
/*
#include <winrt/builders/Windows.UI.Xaml.Controls.Button.h>
#include <winrt/builders/Windows.UI.Xaml.Controls.StackPanel.h>
#include <winrt/builders/Windows.UI.Xaml.Application.h>
*/
#include <winrt/builders/RuntimeComponent1.Class.h>
#include <winrt/formatters/RuntimeComponent1.MyEnum.h>
#include <winrt/builders/helpers.h>
#include <iostream>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <type_traits>

#include <winrt/Windows.Web.Http.Headers.h>
#include <winrt/openapi/SwaggerPetstore-OpenAPI3.0.h>
#include <winrt/Windows.Data.Json.h>

using namespace winrt;
using namespace Windows::UI::Xaml;
using namespace Windows::Web::Http;
using namespace Windows::Data::Json;
using namespace Windows::Storage::Streams;
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
using element_type_IVector = typename std::remove_cv_t<typename std::remove_reference_t<element_type_IVector_cvref<T>>>;

template<typename T>
using key_type_IMap_cvref = typename FunctionTraits<decltype(&T::Insert)>::template NthArg<0>;

template<typename T>
using value_type_IMap_cvref = typename FunctionTraits<decltype(&T::Insert)>::template NthArg<1>;

template<typename T>
using key_type_IMap = typename std::remove_cv_t<typename std::remove_reference_t<key_type_IMap_cvref<T>>>;

template<typename T>
using value_type_IMap = typename std::remove_cv_t<typename std::remove_reference_t<value_type_IMap_cvref<T>>>;

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
  template<typename K, typename V>
  auto f(std::initializer_list<std::initializer_list<std::pair<const K, V>>> v)
  {
    auto m = winrt::single_threaded_vector<winrt::Windows::Foundation::Collections::IMap<K, V>>({});
    for (auto& e : v) {
      m.Append(winrt::single_threaded_map<K, V>(std::unordered_map<K, V>(e)));
    }
    return m;
  }

  MainPage::MainPage()
  {
    InitializeComponent();
  }

  void test_f()
  {
        f<hstring, hstring>({});
        f<hstring, hstring>({
          {{ }},
          {{ }},
          });

        f<hstring, hstring>({
          {{ L"a", L"b" }},
          {{ L"c", L"d" }},
          });


        auto c = winrt::RuntimeComponent1::builders::Class()
          .MyProperty(42)
          .StringVector({ L"foo", L"bar" })
          .StringMapVector({ 
            winrt::builders::make_map<hstring, hstring>({ {L"a", L"b"}}),
            winrt::builders::make_map<hstring, hstring>({ {L"c", L"d"}}),
            })
          .Add_IntEvent([](winrt::Windows::Foundation::IInspectable, int32_t){})
          .Add_TypedEvent([](auto&, float){})
          ;

        // C++/WinRT object creation, property setters, and children
        auto boringPanel = Controls::StackPanel();
        boringPanel.Width(400);
        boringPanel.Height(40);
        auto boringButton = Controls::Button();
        boringButton.Content(winrt::box_value(L"'Sup"));

/*        // CppWinRT.Builders style!
        auto coolPanel = Controls::builders::StackPanel()
          .Width(400)
          .Height(40)
          .Children({
              Controls::builders::Button()
              .Content(winrt::box_value(L"'Sup!"))
            });
  */      
    }

    int32_t MainPage::MyProperty()
    {
        throw hresult_not_implemented();
    }

    void MainPage::MyProperty(int32_t /* value */)
    {
        throw hresult_not_implemented();
    }

    winrt::fire_and_forget MainPage::ClickHandler(IInspectable const&, RoutedEventArgs const&)
    {
      constexpr auto cFoo = winrt::RuntimeComponent1::MyEnum::Foo;
      using formatter = std::formatter<winrt::RuntimeComponent1::MyEnum, wchar_t>;
      
      auto strFoo = std::vformat(L"{}", std::make_wformat_args(cFoo));
      auto strFoo2 = std::format(L"{}", cFoo);

      constexpr auto cStrFoo = formatter::to_string(cFoo);
      
      constexpr auto cParsedFoo = winrt::from_string<winrt::RuntimeComponent1::MyEnum>(L"Foo");
      constexpr auto cParsedFoo2 = winrt::from_string<winrt::RuntimeComponent1::MyEnum>(cStrFoo);
      UNREFERENCED_PARAMETER(cParsedFoo);
      static_assert(cFoo == cParsedFoo2);

      auto y = winrt::from_string<winrt::RuntimeComponent1::MyEnum>(strFoo);
      
      // constexpr auto bad = winrt::from_string<winrt::RuntimeComponent1::MyEnum>(L"asodfji"); // must fail to compile

      assert(cFoo == y);
      myButton().Content(box_value(strFoo));

      {
        using namespace winrt::OpenApi::SwaggerPetstoreOpenAPI30;
        /*
        struct FakeHttpClient {
          HttpStatusCode ResponseStatusCode{};
          ServerEnvironment Environment{};
          wil::com_task<HttpResponseMessage> SendRequestAsync(HttpRequestMessage const& msg) {
            auto response = HttpResponseMessage(ResponseStatusCode);
            const auto uri = msg.RequestUri().AbsoluteUri();
            const auto config = std::find_if(ServerConfigList.begin(),
              ServerConfigList.end(),
              [env = this->Environment](auto&& c) { return c.environment == env; });
            if (!uri.starts_with(config->uri))
            {
              throw winrt::hresult_invalid_argument(L"Invalid URI");
            }
            auto mockPlugin = Plugin();
            mockPlugin.plugin_id = L"plugin_id";
            mockPlugin.catalog_id = L"catalog_id";
            mockPlugin.plugin_name = L"plugin_name";
            mockPlugin.name_for_human = L"name_for_human";
            mockPlugin.description_for_model = L"description_for_model";
            mockPlugin.description_for_human = L"description_for_human";
            mockPlugin.category = L"category";
            mockPlugin.manifest_string = L"manifest_string";
            mockPlugin.logo_url = L"logo_url";
            mockPlugin.bing_image_url = L"bing_image_url";
            mockPlugin.version = L"version";
            auto mockJsonResponse = mockPlugin.ToJsonValue();
            auto mockJsonResponseString = mockJsonResponse.Stringify();
            response.Content(HttpStringContent(mockJsonResponseString, UnicodeEncoding::Utf8, L"application/json"));

            co_return response;
          }
        };

        // custom http client
        static_assert(std::is_same_v<wil::com_task<Plugin>, decltype(SkillsAsync(std::declval<FakeHttpClient>(), L"pluginId", L"version"))>);
        // custom http client and server environment
        static_assert(std::is_same_v<wil::com_task<Plugin>, decltype(SkillsAsync<FakeHttpClient, ServerEnvironment::PROD_environment>(std::declval<FakeHttpClient>(), L"pluginId", L"version"))>);
        // stock http client, custom server environment
        static_assert(std::is_same_v<wil::com_task<Plugin>, decltype(SkillsAsync<ServerEnvironment::PROD_environment>(L"pluginId", L"version"))>);
        auto fakeClient = FakeHttpClient{};
        fakeClient.ResponseStatusCode = HttpStatusCode::Ok;
        auto plugin = co_await SkillsAsync(fakeClient, L"pluginId", L"version");
        auto plugin_id = plugin.plugin_id;
        assert(plugin_id == L"plugin_id");

        fakeClient.Environment = ServerEnvironment::PROD_environment;
        plugin = co_await SkillsAsync<decltype(fakeClient), ServerEnvironment::PROD_environment>(fakeClient, L"pluginId", L"version");

        try {
          plugin = co_await SkillsAsync(fakeClient, L"pluginId", L"version");
        }
        catch (winrt::hresult_invalid_argument const& e) {
          assert(e.code() == E_INVALIDARG);
        }
        catch (...) {
          assert(false);
        }

        fakeClient.Environment = ServerEnvironment::Default;
        fakeClient.ResponseStatusCode = HttpStatusCode::InternalServerError;
        try {
          plugin = co_await SkillsAsync(fakeClient, L"pluginId", L"version");
          assert(false);
        }
        catch (winrt::hresult_error const& e) {
          assert(e.code() == HTTP_E_STATUS_SERVER_ERROR);
        }
        catch (...) {
          assert(false);
        }
        */


        try {
            auto apikey = winrt::OpenApi::SwaggerPetstoreOpenAPI30::Api_key(L"special-key");
            auto inventory = co_await winrt::OpenApi::SwaggerPetstoreOpenAPI30::GetInventoryAsync(apikey);

            for (auto const& [k, v] : inventory) {
                std::wcout << k.c_str() << L" : " << v.GetNumber() << std::endl;
            }
            auto oauth = winrt::OpenApi::SwaggerPetstoreOpenAPI30::Petstore_auth(L"the_petstore_auth_token");
            /*
            inventory = co_await winrt::OpenApi::SwaggerPetstoreOpenAPI30::GetInventoryAsync(oauth);
            for (auto const& [k, v] : inventory) {
                std::wcout << k.c_str() << L" : " << v.GetNumber() << std::endl;
            }
            */

            winrt::OpenApi::SwaggerPetstoreOpenAPI30::Pet pet;
            auto available_pets = co_await winrt::OpenApi::SwaggerPetstoreOpenAPI30::FindPetsByStatusAsync(L"available", oauth);
            auto first = available_pets[0];
            static_assert(std::is_same_v<decltype(first), winrt::OpenApi::SwaggerPetstoreOpenAPI30::Pet>);

            auto order = winrt::OpenApi::SwaggerPetstoreOpenAPI30::Order();
            order.id = 10;
            order.petId = first.id;
            order.quantity = 1;

            order.status = L"placed";
          
            auto order2 = co_await winrt::OpenApi::SwaggerPetstoreOpenAPI30::PlaceOrderAsync(order);
            assert(order2.id == order.id);

            auto deleted = co_await winrt::OpenApi::SwaggerPetstoreOpenAPI30::DeleteOrderAsync(order2.id);
            assert(deleted);
        }
        catch (winrt::hresult_error const& e) {
            std::wcout << e.message().c_str() << std::endl;
        }

      }
    }
}
