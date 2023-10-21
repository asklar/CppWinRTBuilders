#include <winrt/Windows.Foundation.h>
#include <winrt/DependencyInjection.h>

using namespace winrt;
using namespace Windows::Foundation;

struct ConcreteStringable : winrt::implements<ConcreteStringable, winrt::Windows::Foundation::IStringable> {
  winrt::hstring ToString() {
		return L"ConcreteStringable";
	}
  static auto Create() { return winrt::make_self<ConcreteStringable>(); }
};

struct ConcreteClosable : winrt::implements<ConcreteClosable, winrt::Windows::Foundation::IClosable> {
  void Close() {};

  ConcreteClosable(winrt::Windows::Foundation::IStringable&) {}
  static auto Create(winrt::Windows::Foundation::IStringable s) { return winrt::make_self<ConcreteClosable>(s); }
};

int main() {
  // Initialize WinRT
  winrt::init_apartment();

  DependencyInjection::DependencyContainer container;
  container.RegisterDependency<IStringable, ConcreteStringable>();
  container.RegisterDependency<IClosable, ConcreteClosable>();

  auto closable = container.ResolveDependency<IClosable>();
  return 0;
}
