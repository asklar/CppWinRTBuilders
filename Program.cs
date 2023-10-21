﻿using MiddleweightReflection;
using System.Reflection;

//const string Windows_winmd = @"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.19041.0\Windows.winmd";
var context = new MrLoadContext(false);
context.FakeTypeRequired += (sender, e) =>
{
  var ctx = (MrLoadContext)sender!;
  if (e.AssemblyName == "Windows.Foundation.FoundationContract" || e.AssemblyName == "Windows.Foundation.UniversalApiContract")
  {
    e.ReplacementType = ctx.GetTypeFromAssembly(e.TypeName, "Windows");
  }
};

//var windows_winmd = context.LoadAssemblyFromPath(Windows_winmd);

// var winmds = winmdPaths.Select(winmdPath => context.LoadAssemblyFromPath(winmdPath)).ToList();
// ToList realizes the list which is needs to happen before FinishLoading is called


var outFolder = string.Empty;
var namespaces = new List<string>();

foreach (var arg in args)
{
  if (arg.StartsWith("-n:"))
  {
    namespaces.Add(arg.Substring(3));
  }
  else if (arg.StartsWith("-o:"))
  {
    outFolder = arg.Substring(3);
  }
  else if (arg.StartsWith("-in:"))
  {
    context.LoadAssemblyFromPath(arg.Substring(4));
  }
}

if (outFolder == string.Empty)
{
  throw new ArgumentException("Output folder cannot be empty, specify -o:");
}


context.FinishLoading();

if (context.LoadedAssemblies.Count == 0)
{
  throw new ArgumentException("No assemblies specified, specify them with -in:");
}

var winrtFolder = Path.Combine(outFolder, "winrt");
var buildersFolder = Path.Combine(outFolder, "winrt", "builders");
var formattersFolder = Path.Combine(outFolder, "winrt", "formatters");
Directory.CreateDirectory(buildersFolder);
Directory.CreateDirectory(formattersFolder);

foreach (var asm in context.LoadedAssemblies)
{
  var typesToCodegenFor = asm.GetAllTypes().Where(t => HasCtor(t) && PassesTypeFilter(t) && Helpers.HasSetters(t));

  foreach (var type in typesToCodegenFor)
  {
    var bt = new CppWinRT.Builders.BuilderTemplate(type);
    var s = bt.TransformText();
    File.WriteAllText(Path.Combine(buildersFolder, type.GetFullName() + ".h"), s);
  }

  var enumsToCodegenFormattersFor = asm.GetAllTypes().Where(t => t.IsEnum && PassesTypeFilter(t));
  foreach (var type in enumsToCodegenFormattersFor)
  {
    var enumFormatters = new CppWinRT.Builders.EnumFormattingTemplate(type);
    var ef = enumFormatters.TransformText();
    File.WriteAllText(Path.Combine(formattersFolder, type.GetFullName() + ".h"), ef);
  }


  var headers = typesToCodegenFor.Select(t => "#include <winrt/builders/" + t.GetFullName() + ".h>");
  var assemblyIncludes = @"#pragma once
// This file was automatically generated by CppWinRT.Builders

" + string.Join("\n", headers);
  var filename = asm.FullName ?? Path.GetFileNameWithoutExtension(asm.Location);
  File.WriteAllText(Path.Combine(buildersFolder, filename + ".h"), assemblyIncludes);
}

File.WriteAllText(Path.Combine(buildersFolder, "helpers.h"), @"#pragma once
// This file was automatically generated by CppWinRT.Builders
namespace winrt::builders {
  template<typename K, typename V = K>
  auto make_map(std::initializer_list<std::pair<const K, V>>&& value) {
    auto map = std::unordered_map<K, V>(std::move(value));
    return winrt::single_threaded_map<K, V>(std::move(map));
  }
}
");

var toolDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
File.Copy(Path.Combine(toolDir, "DependencyInjection.h"), Path.Combine(winrtFolder, "DependencyInjection.h"), true);

File.WriteAllText(Path.Combine(formattersFolder, "helpers.h"), @"#pragma once
// This file was automatically generated by CppWinRT.Builders

#if defined(__cpp_concepts) && __cpp_concepts >= 201907L

template <typename T>
concept winrt_enum = std::same_as<winrt::impl::category_t<T>, winrt::impl::enum_category>;

namespace winrt::formatters::impl
{
  template<winrt_enum T>
  struct traits {};
}

  template <winrt_enum T>
struct std::formatter<T, wchar_t> : ::std::formatter<std::wstring_view, wchar_t>
{
    template <typename FormatContext>
    auto format(const T& value, FormatContext& ctx) const
    {
        auto str = to_string(value);
        return std::formatter<std::wstring_view, wchar_t>::format(str, ctx);
    }

    static constexpr std::wstring_view to_string(const T& value);
    static constexpr T from_string(std::wstring_view value);
};

namespace winrt
{
template <winrt_enum T>
constexpr T from_string(std::wstring_view value)
{
    return std::formatter<T, wchar_t>::from_string(value);
}
}

#else
#error CppWinRT.Builders formatters rely on C++ Concepts, which your build does not support. Please upgrade to a compiler that supports C++20 Concepts.
#endif
");

bool PassesTypeFilter(MrType t)
{
  bool passes = false;

  if (namespaces.Count == 0) { passes = true; }
  else
  {
    var _ns = t.GetNamespace();
    foreach (var ns in namespaces)
    {
      if (ns.EndsWith('*'))
      {
        if (_ns.StartsWith(ns.Substring(0, ns.Length - 1)))
        {
          return true;
        }
      }
      else
      {
        if (_ns.Equals(ns, StringComparison.OrdinalIgnoreCase))
        {
          return true;
        }
      }
    }
  }

  return passes;
}

bool HasCtor(MrType t)
{
  t.GetMethodsAndConstructors(out var methods, out var ctors);
  var publicCtors = ctors.Where(x => x.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Public));
  return publicCtors.Count() != 0;
}

public static class Helpers
{
  public static string GetCppTypeName(MrType t)
  {
    var primitiveTypes = new Dictionary<string, string>()
            {
                { "System.String", "winrt::hstring" },
                { "System.Boolean", "bool" },
                { "System.Byte", "uint8_t" },
                { "System.UInt16", "uint16_t" },
                { "System.UInt32", "uint32_t" },
                { "System.UInt64", "uint64_t" },
                { "System.Int16", "int16_t" },
                { "System.Int32", "int32_t" },
                { "System.Int64", "int64_t" },
                { "System.Double", "double" },
                { "System.Single", "float" },
                { "System.Object", "winrt::Windows::Foundation::IInspectable" },
                // see https://github.com/microsoft/cppwinrt/blob/master/cppwinrt/type_writers.h#L95
                { "Windows.Foundation.Numerics.Matrix4x4", "winrt::Windows::Foundation::Numerics::float4x4" },
                { "Windows.Foundation.Numerics.Matrix3x2", "winrt::Windows::Foundation::Numerics::float3x2" },
                { "Windows.Foundation.Numerics.Plane", "winrt::Windows::Foundation::Numerics::plane" },
                { "Windows.Foundation.Numerics.Quaternion", "winrt::Windows::Foundation::Numerics::quaternion" },
                { "Windows.Foundation.Numerics.Vector2", "winrt::Windows::Foundation::Numerics::float2" },
                { "Windows.Foundation.Numerics.Vector3", "winrt::Windows::Foundation::Numerics::float3" },
                { "Windows.Foundation.Numerics.Vector4", "winrt::Windows::Foundation::Numerics::float4" },
            };

    string fullname = t.GetFullName();
    if (fullname == "System.Nullable`1")
    {
      return GetCppTypeName(t.GetGenericTypeParameters().First());
    }
    if (primitiveTypes.ContainsKey(fullname))
    {
      return primitiveTypes[fullname];
    }

    if (fullname.Length > 2 && fullname[fullname.Length - 2] == '`')
    {
      var generic = fullname.Substring(0, fullname.Length - 2);
      var genericCpp = $"winrt::{generic.Replace(".", "::")}";
      var args = string.Join(", ", t.GetGenericArguments().Select(GetCppTypeName));
      return $"{genericCpp}<{args}>";
    }
    if (fullname.Equals(IMap, StringComparison.OrdinalIgnoreCase))
    {
      var k = GetCppTypeName(t.GetGenericArguments().First());
      var v = GetCppTypeName(t.GetGenericArguments().Skip(1).First());
      return $"winrt::Windows::Foundation::Collections::IMap<{k}, {v}>";
    }
    return $"winrt::{t.GetPrettyFullName().Replace(".", "::")}";
  }

  public static IEnumerable<MrProperty> GetAllSetters(MrType type)
  {
    var s = type.GetProperties().Where(HasInstanceSetter);
    var baseType = type.GetBaseType();
    if (baseType != null)
    {
      s = s.Concat(GetAllSetters(baseType));
    }
    return s;
  }

  public static IEnumerable<MrProperty> GetAllCollectionSetters(MrType type)
  {
    var s = type.GetProperties().Where(p => HasInstanceGetter(p) && IsCollectionProperty(p));
    var baseType = type.GetBaseType();
    if (baseType != null)
    {
      s = s.Concat(GetAllCollectionSetters(baseType));
    }
    return s;
  }

  public static IEnumerable<MrEvent> GetAllEvents(MrType type)
  {
    IEnumerable<MrEvent> s = type.GetEvents().ToList();
    var baseType = type.GetBaseType();
    if (baseType != null)
    {
      s = s.Concat(GetAllEvents(baseType));
    }
    return s;
  }

  public static string GetCppEventHandlerType(MrEvent evt)
  {
    return GetCppTypeName(evt.GetEventType());
  }

  private static Dictionary<MrType, bool> collectionProps = new();
  public const string IVector = "Windows.Foundation.Collections.IVector`1";
  public const string IMap = "Windows.Foundation.Collections.IMap`2";
  public const string IObservableVector = "Windows.Foundation.Collections.IObservableVector`1";

  //private const string IIterable = "Windows.Foundation.Collections.IIterable`1";

  private static bool IsCollectionProperty(MrProperty p)
  {
    var type = p.GetPropertyType();
    if (collectionProps.ContainsKey(type)) return collectionProps[type];
    bool isIVector = GetInterfaceFromType(type, IVector) != null;
    var isIMap = GetInterfaceFromType(type, IMap) != null;

    // properties should never be array typed (T[]), since the projected array doesn't have a way to reflect changes to WinRT.
    if (isIVector || isIMap)
    {
      collectionProps[type] = true;
      return true;
    }
    collectionProps[type] = false;
    return false;
  }

  public static MrType? GetInterfaceFromType(MrType type, string interfaceName)
  {
    return type.GetFullName().Equals(interfaceName, StringComparison.OrdinalIgnoreCase) ? type : type.GetInterfaces().FirstOrDefault(i => i.GetFullName() == interfaceName);
  }

  struct CollectionGeneric
  {
    public string Name { get; init; }
    public int NArgs { get; init; }

    public CollectionGeneric(string name, int nArgs)
    {
      Name = name;
      NArgs = nArgs;
    }
  }

  public static string GetCppCollectionElementType(MrType type)
  {
    var kind = collectionProps[type];


    var collectionTypes = new CollectionGeneric[]
    {
        new (IObservableVector, 1),
        new (IVector, 1),
        new (IMap, 2),
    };

    foreach (var e in collectionTypes)
    {
      var interface_ = GetInterfaceFromType(type, e.Name);
      if (interface_ != null)
      {
        var args = interface_.GetGenericArguments().Select(GetCppTypeName);
        switch (e.NArgs)
        {
          case 1:
            return args.First();
          case 2:
            return $"std::pair<{string.Join(", ", args)}>";
          default:
            return $"std::tuple<{string.Join(", ", args)}>";
        }
      }
    }

    throw new Exception();
  }

  public static bool HasSetters(MrType type)
  {
    var s = type.GetProperties().Where(HasInstanceSetter);
    if (s.Count() != 0) return true;
    var baseType = type.GetBaseType();
    if (baseType != null)
    {
      return HasSetters(baseType);
    }
    return false;
  }

  private static bool HasInstanceSetter(MrProperty p)
  {
    return p.Setter != null &&
      p.Setter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Public) &&
      !p.Setter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Static);
  }

  private static bool HasInstanceGetter(MrProperty p)
  {
    return p.Getter != null &&
      p.Getter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Public) &&
      !p.Getter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Static);
  }


}

namespace CppWinRT.Builders
{
  public partial class EnumFormattingTemplate
  {
    public EnumFormattingTemplate(MrType t)
    {
      _type = t;
    }

    private MrType _type;
  }

  public partial class BuilderTemplate
  {
    public BuilderTemplate(MrType t)
    {
      _type = t;
    }

 
    private MrType _type;

  }
}