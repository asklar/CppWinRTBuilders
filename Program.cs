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
var buildersFolder = Path.Combine(outFolder, "winrt", "builders");
Directory.CreateDirectory(buildersFolder);

foreach (var asm in context.LoadedAssemblies)
{
  var typesToCodegenFor = asm.GetAllTypes().Where(t => HasCtor(t) && PassesTypeFilter(t) && CppWinRT.Builders.BuilderTemplate.HasSetters(t));

  foreach (var type in typesToCodegenFor)
  {
    var bt = new CppWinRT.Builders.BuilderTemplate(type);
    var s = bt.TransformText();
    File.WriteAllText(Path.Combine(buildersFolder, type.GetFullName() + ".h"), s);
  }
}

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


namespace CppWinRT.Builders
{
  public partial class BuilderTemplate
  {
    public BuilderTemplate(MrType t)
    {
      _type = t;
    }

    static IEnumerable<MrProperty> GetAllSetters(MrType type)
    {
      var s = type.GetProperties().Where(HasInstanceSetter);
      var baseType = type.GetBaseType();
      if (baseType != null)
      {
        s = s.Concat(GetAllSetters(baseType));
      }
      return s;
    }

    static IEnumerable<MrProperty> GetAllCollectionSetters(MrType type)
    {
      var s = type.GetProperties().Where(p => HasInstanceGetter(p) && IsCollectionProperty(p));
      var baseType = type.GetBaseType();
      if (baseType != null)
      {
        s = s.Concat(GetAllCollectionSetters(baseType));
      }
      return s;
    }

    private static Dictionary<MrType, bool> collectionProps = new();
    private const string IVector = "Windows.Foundation.Collections.IVector`1";
    private const string IMap = "Windows.Foundation.Collections.IMap`2";
    //private const string IIterable = "Windows.Foundation.Collections.IIterable`1";

    private static bool IsCollectionProperty(MrProperty p)
    {
      var type = p.GetPropertyType();
      if (collectionProps.ContainsKey(type)) return collectionProps[type];
      bool isIVector = GetIVector(type) != null;
      var isIMap = GetIMap(type) != null;

      // properties should never be array typed (T[]), since the projected array doesn't have a way to reflect changes to WinRT.
      if (isIVector || isIMap)
      {
        collectionProps[type] = true;
        return true;
      }
      collectionProps[type] = false;
      return false;
    }

    private static MrType? GetIMap(MrType type)
    {
      return type.GetFullName().Equals(IMap, StringComparison.OrdinalIgnoreCase) ? type : type.GetInterfaces().FirstOrDefault(i => i.GetFullName() == IMap);
    }

    private static MrType? GetIVector(MrType type)
    {
      return type.GetFullName().Equals(IVector, StringComparison.OrdinalIgnoreCase) ? type : type.GetInterfaces().FirstOrDefault(i => i.GetFullName() == IVector);
    }

    private static string GetCppCollectionElementType(MrType type)
    {
      var kind = collectionProps[type];
      var ivector = GetIVector(type);
      if (ivector != null)
      {
        return GetCppTypeName(ivector.GetGenericArguments().First());
      }

      var imap = GetIMap(type);
      if (imap != null)
      {
        return $"std::pair<{GetCppTypeName(imap.GetGenericArguments()[0])}, {GetCppTypeName(imap.GetGenericArguments()[1])}>";
      }

      throw new Exception();
    }

    private static string GetInsertOrAppend(MrType type)
    {
      if (GetIVector(type) != null)
      {
        return $"Append(v)";
      }
      else if (GetIMap(type) != null)
      {
        return $"Insert(v.first, v.second)";
      }
      throw new ArgumentException();
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

    private static string GetCppTypeName(MrType t)
    {
      var primitiveTypes = new Dictionary<string, string>()
            {
                { "System.String", "winrt::hstring" },
                { "System.Boolean", "bool" },
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

      if (t.GetFullName() == "System.Nullable`1")
      {
        return GetCppTypeName(t.GetGenericTypeParameters().First());
      }
      if (primitiveTypes.ContainsKey(t.GetFullName()))
      {
        return primitiveTypes[t.GetFullName()];
      }
      return $"winrt::{t.GetFullName().Replace(".", "::")}";
    }

    private MrType _type;

  }
}