#include <functional>
#include <unordered_map>
#include <tuple>
#include <winrt/Windows.Foundation.h>

namespace winrt::DependencyInjection {
  class DependencyContainer {
  private:
    static uint64_t hash(winrt::guid const& g) {
      // simple hash of a guid: XOR the top 64 bits with the bottom 64 bits
      auto p = reinterpret_cast<uint64_t const*>(&g);
      return p[0] ^ p[1];
    }
    std::unordered_map<uint64_t, std::function<winrt::Windows::Foundation::IInspectable()>> dependencyFactories;
    std::unordered_map<uint64_t, winrt::Windows::Foundation::IInspectable> dependencies;

  public:
    // Register a dependency in the container with a factory function
    template <typename Interface, typename ConcreteType>
    void RegisterDependency() {
      auto factory = [this]() {
        return this->CreateDependency<ConcreteType>();
        };
      winrt::guid interfaceGuid = winrt::guid_of<Interface>();
      dependencyFactories[hash(interfaceGuid)] = factory;
    }

    // Resolve a dependency from the container, constructing it dynamically
    template <typename Interface>
    Interface ResolveDependency() {
      winrt::guid interfaceGuid = winrt::guid_of<Interface>();
      if (dependencies.find(hash(interfaceGuid)) == dependencies.end()) {
        auto factory = dependencyFactories[hash(interfaceGuid)];
        auto instance = factory().as<Interface>();
        dependencies[hash(interfaceGuid)] = instance;
        return instance;
      }
      else {
        return dependencies[hash(interfaceGuid)].as<Interface>();
      }
    }

  private:

    template<typename T>
    struct CreateHelper;

    template<typename... Ts>
    struct CreateHelper<std::tuple<Ts...>>
    {
      static auto Create(DependencyContainer& dc)
      {
        return std::make_tuple(dc.ResolveDependency<Ts>()...);
      }
    };

    template<typename F>
    struct function_traits;

    template<typename R, typename... Args>
    struct function_traits<R(__cdecl*)(Args...)> {
      using return_type = R;
      using args_tuple = std::tuple<Args...>;
    };

    // Helper function to create a concrete dependency
    template <typename Concrete>
    winrt::Windows::Foundation::IInspectable CreateDependency() {
      using dependencies_type = function_traits<decltype(&Concrete::Create)>::args_tuple;

      auto arg_tuple = CreateHelper<dependencies_type>::Create(*this);

      auto result = std::apply([](auto&&... args) { return winrt::make<Concrete>(args...); }, arg_tuple);
      return result;
    }

  };
}

// Sample 
/*
int main() {
  // Initialize WinRT
  winrt::init_apartment();

  DependencyContainer container;
  container.RegisterDependency<IStringable, ConcreteStringable>();
  container.RegisterDependency<IClosable, ConcreteClosable>();

  auto closable = container.ResolveDependency<IClosable>();
  return 0;
}
*/