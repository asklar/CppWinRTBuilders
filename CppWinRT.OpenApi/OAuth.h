#pragma once
#include <string>
#include <string_view>
#include <numeric>
#include <winrt/windows.foundation.h>
#include <winrt/windows.applicationmodel.activation.h>
#include <winrt/windows.web.http.h>
#include <winrt/windows.web.http.headers.h>
#include <winrt/windows.system.h>
#include <winrt/windows.data.json.h>
#include <wil/coroutine.h>
#include <wil/resource.h>

struct SimpleOAuth : std::enable_shared_from_this<SimpleOAuth>
{
    struct Result
    {
        std::wstring AccessToken;
        std::wstring TokenType;
        std::wstring Scope;
        std::wstring RefreshToken;
        winrt::Windows::Foundation::DateTime ExpiresOn;
        winrt::Windows::Foundation::DateTime RefreshTokenExpiresOn;

        bool IsValid() const
        {
            return !AccessToken.empty() &&
                (ExpiresOn.time_since_epoch().count() == 0 || winrt::clock::now() < ExpiresOn);
        }

        bool Refresh()
        {
            throw winrt::hresult_not_implemented(L"OAuth Token Refresh not implemented");
        }
    };

    struct Error : winrt::hresult_error
    {
        Error(std::wstring_view error, std::wstring_view error_description) :
            winrt::hresult_error(E_FAIL, error_description),
            ErrorMsg(error)
        {}
        std::wstring ErrorMsg;
    };
    std::wstring AuthorizationUrl;
    std::wstring TokenUrl;
    std::wstring ClientId;
    std::wstring ClientSecret;
    std::wstring RedirectUri;

#ifdef WINRT_Windows_ApplicationModel_Activation_H
    static bool OnReceivedCode(winrt::Windows::ApplicationModel::Activation::IActivatedEventArgs const& args)
    {
        if (args.Kind() == winrt::Windows::ApplicationModel::Activation::ActivationKind::Protocol)
        {
            auto protocolArgs = args.try_as<winrt::Windows::ApplicationModel::Activation::ProtocolActivatedEventArgs>();
            if (protocolArgs)
            {
                const auto uri = protocolArgs.Uri();
                return OnReceivedCode(uri);
            }
        }
    }
#ifdef WINRT_Microsoft_Windows_AppLifecycle_H
    static bool OnReceivedCode(winrt::Microsoft::Windows::AppLifecycle::AppActivationArguments const& args)
    {
        if (args.Kind() == winrt::Microsoft::Windows::AppLifecycle::ExtendedActivationKind::Protocol)
        {
            auto protocolArgs = args.Data().try_as<winrt::Windows::ApplicationModel::Activation::IProtocolActivatedEventArgs>();
            if (protocolArgs)
            {
                const auto uri = protocolArgs.Uri();
                return OnReceivedCode(uri);
            }
        }
    }
#endif // WINRT_Microsoft_Windows_AppLifecycle_H
#endif // WINRT_Windows_ApplicationModel_Activation_H

    static bool OnReceivedCode(winrt::Windows::Foundation::Uri const& uri)
    {
        const auto qp = uri.QueryParsed();
        winrt::hstring code;
        winrt::hstring state;
        for (const auto& pair : qp)
        {
            if (pair.Name() == L"state")
            {
                state = pair.Value();
            }
            else if (pair.Name() == L"code")
            {
                code = pair.Value();
            }
        }
        if (!state.empty() && !code.empty() && _oauthMap.find(state.c_str()) != _oauthMap.end())
        {
            auto oauth = _oauthMap[state.c_str()];
            if (uri.AbsoluteUri().starts_with(oauth->RedirectUri))
            {
                _oauthMap.erase(state.c_str());
                oauth->OnReceivedCode(code);
                return true;
            }
        }
        return false;
    }

    wil::com_task<Result> Authenticate(std::vector<std::wstring> const& scopes, std::wstring state = {})
    {
        auto lifetime = shared_from_this();
        wchar_t uuidStr[40]{};
        if (state.empty())
        {
            GUID guid{};
            if (FAILED(CoCreateGuid(&guid)))
            {
                throw winrt::hresult_error(E_FAIL);
            }
            StringFromGUID2(guid, uuidStr, ARRAYSIZE(uuidStr));
            state = std::wstring(uuidStr + 1, 36);
        }
        _oauthMap[state] = lifetime;

        auto scopesStr = std::accumulate(scopes.begin(), scopes.end(), std::wstring(), [](auto&& a, auto&& b) { return a + L" " + b; });
        auto encode = winrt::Windows::Foundation::Uri::EscapeComponent;
        auto oauthAuthorizationUrl = std::format(L"{}?client_id={}&response_type=code&redirect_uri={}&response_mode=query&scope={}&state={}",
            AuthorizationUrl,
            encode(ClientId),
            encode(RedirectUri),
            encode(scopesStr),
            encode(state)
        );

        if (co_await winrt::Windows::System::Launcher::LaunchUriAsync(winrt::Windows::Foundation::Uri(oauthAuthorizationUrl)))
        {
            // we must wait until the app is resumed which will happen when the browser protocol-launches the app
            co_await winrt::resume_on_signal(Signal.get());

            auto oauthTokenUrl = std::format(L"{}?client_id={}&client_secret={}&code={}",
                TokenUrl,
                encode(ClientId),
                encode(ClientSecret),
                encode(_code)
            );

            _code.clear();

            auto httpClient = winrt::Windows::Web::Http::HttpClient();
            auto request = winrt::Windows::Web::Http::HttpRequestMessage(winrt::Windows::Web::Http::HttpMethod::Post(), winrt::Windows::Foundation::Uri(oauthTokenUrl));
            request.Headers().Append(L"Accept", L"application/json");
            auto response = co_await httpClient.SendRequestAsync(request);
            if (response.IsSuccessStatusCode()) {
                auto json = co_await response.Content().ReadAsStringAsync();
                auto obj = winrt::Windows::Data::Json::JsonObject::Parse(json);
                if (obj.HasKey(L"error")) {
                    auto error = obj.GetNamedString(L"error");
                    auto error_description = obj.GetNamedString(L"error_description");
                    throw Error(error, error_description);
                }
                auto access_token = obj.GetNamedString(L"access_token");
                auto token_type = obj.GetNamedString(L"token_type");
                auto scope = obj.GetNamedString(L"scope");
                auto result = Result{ access_token.c_str(), token_type.c_str(), scope.c_str() };
                if (obj.HasKey(L"expires_in")) {
                    auto expires_in = obj.GetNamedNumber(L"expires_in");
                    auto refresh_token = obj.GetNamedString(L"refresh_token");
                    auto refresh_token_expires_in = obj.GetNamedNumber(L"refresh_token_expires_in");
                    result.ExpiresOn = winrt::clock::now() + winrt::Windows::Foundation::TimeSpan(std::chrono::seconds(static_cast<int>(expires_in)));
                    result.RefreshToken = refresh_token.c_str();
                    result.RefreshTokenExpiresOn = winrt::clock::now() + winrt::Windows::Foundation::TimeSpan(std::chrono::seconds(static_cast<int>(refresh_token_expires_in)));
                }
                co_return result;
            }
            else
            {
                auto hresult = MAKE_HRESULT(SEVERITY_ERROR, FACILITY_HTTP, static_cast<int>(response.StatusCode()));
                throw winrt::hresult_error(hresult);
            }
        }
    }
private:
    wil::unique_event Signal{ CreateEventW(nullptr, false, false, nullptr) };
    std::wstring _code;
    inline static std::unordered_map<std::wstring, std::shared_ptr<SimpleOAuth>> _oauthMap;

    void OnReceivedCode(winrt::hstring const& code)
    {
        _code = code.c_str();
        Signal.SetEvent();
    }
};

