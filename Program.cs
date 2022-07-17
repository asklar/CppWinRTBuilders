using MiddleweightReflection;
using System.Reflection;

const string Windows_winmd = @"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.19041.0\Windows.winmd";
var context = new MrLoadContext(false);
context.FakeTypeRequired += (sender, e) =>
{
  var ctx = (MrLoadContext)sender!;
  if (e.AssemblyName == "Windows.Foundation.FoundationContract" || e.AssemblyName == "Windows.Foundation.UniversalApiContract")
  {
    e.ReplacementType = ctx.GetTypeFromAssembly(e.TypeName, "Windows");
  }
};

var windows_winmd = context.LoadAssemblyFromPath(Windows_winmd);

// var winmds = winmdPaths.Select(winmdPath => context.LoadAssemblyFromPath(winmdPath)).ToList();
// ToList realizes the list which is needs to happen before FinishLoading is called

context.FinishLoading();

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
}

if (outFolder == string.Empty)
{
  throw new ArgumentException("Output folder cannot be empty, specify -o:");
}
var typesToCodegenFor = windows_winmd.GetAllTypes().Where(t => HasCtor(t) && PassesTypeFilter(t));

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

var buildersFolder = Path.Combine(outFolder, "winrt", "builders");
Directory.CreateDirectory(buildersFolder);
foreach (var type in typesToCodegenFor)
{
  var bt = new CppWinRTBuilderCodeGen.BuilderTemplate(type);
  var s = bt.TransformText();
  File.WriteAllText(Path.Combine(buildersFolder, type.GetFullName() + ".h"), s);
}


bool HasCtor(MrType t)
{
  t.GetMethodsAndConstructors(out var methods, out var ctors);
  var publicCtors = ctors.Where(x => x.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Public));
  return publicCtors.Count() != 0;
}


namespace CppWinRTBuilderCodeGen
{
  public partial class BuilderTemplate
  {
    public BuilderTemplate(MrType t)
    {
      _type = t;
    }

    IEnumerable<MrProperty> GetSetters()
    {
      var s = _type.GetProperties().Where(HasInstanceSetter);
      return s;
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

    private static bool HasInstanceSetter(MrProperty p)
    {
      return p.Setter != null &&
        p.Setter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Public) &&
        !p.Setter.MethodDefinition.Attributes.HasFlag(MethodAttributes.Static);
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