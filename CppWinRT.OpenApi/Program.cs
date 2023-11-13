using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var outFolder = string.Empty;
var openApiPath = string.Empty;
foreach (var arg in args)
{
  if (arg.StartsWith("-o:"))
  {
    outFolder = arg.Substring(3);
  }
  else if (arg.StartsWith("-in:"))
  {
    openApiPath = arg.Substring(4);
  }
}

if (outFolder == string.Empty)
{
  throw new ArgumentException("Output folder cannot be empty, specify -o:");
}
else if (openApiPath == string.Empty)
{
  throw new ArgumentException("OpenApi file path cannot be empty, specify -in:");
}

// determine whether openApiPath is a url or a file path
JsonObject json;
string specificationUrl = string.Empty;
if (openApiPath.StartsWith("http://") || openApiPath.StartsWith("https://"))
{
  using (var client = new System.Net.Http.HttpClient())
  {
    var jsonText = await client.GetStringAsync(openApiPath);
    json = JsonSerializer.Deserialize<JsonObject>(jsonText)!;
    specificationUrl = openApiPath;
  }
}
else
{
  var jsonText = File.ReadAllText(openApiPath);
  json = JsonSerializer.Deserialize<JsonObject>(jsonText)!;
  specificationUrl = new Uri(openApiPath).AbsoluteUri;
}
var title = json["info"]!["title"]!.ToString();
var version = json["info"]!["version"]!.ToString();

var generator = new CppWinRT.OpenApi.CppWinRTGenerator
{
  Title = title,
  Version = version,
  SpecificationUrl = specificationUrl,
  OpenApiPath = openApiPath,
};

// select a server
var servers = json["servers"]!.AsArray();
foreach (var server in servers)
{
  var url = server!["url"]!.ToString().Trim();
  var description = server["description"]!.ToString().Trim();
  generator.Servers.Add(description, url);
}

generator.ServerUri = generator.Servers.First().Value;

var paths = json["paths"]!.AsObject();
foreach (var path in paths)
{
  var pathName = path.Key;
  var pathValue = path.Value;
  foreach (var method in pathValue!.AsObject().AsEnumerable())
  {
    var methodName = method.Key;
    var methodValue = method.Value;
    //var operationId = methodValue["operationId"]!.ToString();
    var summary = methodValue["summary"]?.ToString().Trim();
    var description = methodValue["description"]?.ToString().Trim();
    var parameters = methodValue["parameters"]?.AsArray() ?? new JsonArray();
    var requestBody = methodValue["requestBody"]?.AsObject();
    var responses = methodValue["responses"]?.AsObject();
    var tags = methodValue["tags"]?.AsArray();
    var security = methodValue["security"]?.AsArray();
    var pathObject = new CppWinRT.OpenApi.Path
    {
      PathUriTemplate = pathName,
      Method = methodName.ToCamelCase(), // convert put to Put
      //OperationId = operationId,
      Summary = summary,
      Description = description,
      Tags = tags?.Select(x => x!.ToString()).ToArray(),
      Security = security?.Select(x => x!.ToString()).ToArray(),
    };
    foreach (var parameter in parameters)
    {
      var parameterObject = new CppWinRT.OpenApi.Parameter
      {
        Name = parameter!["name"]!.ToString(),
        In = parameter["in"]!.ToString(),
        Description = parameter["description"]?.ToString(),
        Required = bool.Parse(parameter["required"]!.ToString()),
        Schema = generator.LookupType(parameter["schema"]!["type"]!.ToString()!),
      };
      pathObject.Parameters.Add(parameterObject);
    }
    if (requestBody != null)
    {
      pathObject.RequestBody = new CppWinRT.OpenApi.Request
      {
        IsRequired = bool.Parse(requestBody!["required"]!.ToString()),
      };
      var content = requestBody["content"]!.AsObject()!;
      var mimeType = content.First();
      var properties = mimeType.Value!["schema"]!["properties"]!.AsObject();
      var bodyParam = properties.Select(p => new CppWinRT.OpenApi.Parameter
      {
        Name = p.Key,
        Schema = generator.LookupType(p.Value["type"]!.ToString()!),
      });
      pathObject.RequestBody.Properties.AddRange(bodyParam);
    }
    if (responses != null)
    {
      var response = responses.First();
      var content = response.Value!["content"]!.AsObject()!;
      var mimeType = content.First();
      var schema = mimeType.Value!["schema"]!;
      if (schema != null)
      {
        var type = schema["type"]!.ToString()!;
        var responseType = generator.LookupType(type);
        pathObject.ResponseType = responseType;
      }
      else if (mimeType.Value != null)
      {
        var example = mimeType.Value!["example"]!.AsObject();
        var typeDef = example.First().Value.AsObject();
        var typeName = example.First().Key;
        pathObject.ResponseType = generator.CreateType("winrt::OpenApi", typeName, typeDef);
      }
    }
    generator.Paths.Add(pathObject);
    //var servers = methodValue["servers"]!.AsArray();
  }
}

var output = generator.TransformText();

var openApiFolder = System.IO.Path.Combine(outFolder, "winrt", "openapi");
Directory.CreateDirectory(openApiFolder);
var filename = title.Split(' ').Select(x => x.ToCamelCase()).Aggregate((x, y) => x + y) + ".h";
// remove all non-filesystem-safe characters
foreach (var c in System.IO.Path.GetInvalidFileNameChars())
{
  filename = filename.Replace(c.ToString(), string.Empty);
}

File.WriteAllText(System.IO.Path.Combine(openApiFolder, filename), output);


namespace CppWinRT.OpenApi
{
  public class CppWinRTType
  {
    public string JsonName { get; set; }
    public string CppWinRTFullName
    {
      get =>
        IsBuiltIn ? CppWinRTName : $"winrt::{Namespace}::{CppWinRTName}";
    }
    public bool IsBuiltIn { get; set; }
    public List<Parameter> Members { get; set; } = new();
    public string Namespace { get; internal set; }

    public string CppWinRTName { get; set; }
  }

  public class Parameter
  {
    public string Name { get; set; }
    public string In { get; set; }
    public string Description { get; set; }
    public bool Required { get; set; }
    public CppWinRTType Schema { get; set; }
  }

  public class Request
  {
    public bool IsRequired { get; set; }
    public List<Parameter> Properties { get; set; } = new();
  }

  public class Path
  {
    public string PathUriTemplate { get; set; }

    public string Name
    {
      // PathUriTemplate is something like "/api/v0.1/skills/{pluginId}/{version}" - we need to get the "skills" part
      get => PathUriTemplate.Split('/')[3];
    }
    public string Method { get; set; }
    //public string OperationId { get; set; }
    public string Summary { get; set; }
    public string Description { get; set; }
    public string[] Tags { get; set; }
    public string[] Security { get; set; }

    public List<Parameter> Parameters { get; } = new();

    public string GetParametersNamesWithHttpClient()
    {
      var allParams = new string[] { "_client" }.Concat(Parameters.Select(p => p.Name));
      if (RequestBody != null)
      {
        allParams = allParams.Concat(RequestBody.Properties.Select(p => p.Name));
      }
      return string.Join(", ", allParams);
    }

    public Request RequestBody;

    public CppWinRTType ResponseType = new();

    public string GetCppName()
    {
      return Name.ToCamelCase() + "Async";
    }

    public string DoxygenComment
    {
      get
      {
        StringBuilder sb = new();
        sb.AppendLine("/**");
        if (!string.IsNullOrEmpty(Summary)) sb.AppendLine($" * @brief {Summary}");
        if (!string.IsNullOrEmpty(Description)) sb.AppendLine($" * {Description}");
        if (!string.IsNullOrEmpty(PathUriTemplate)) sb.AppendLine($" * Url path: {PathUriTemplate}");
        sb.Append(" */");
        var ret = sb.ToString();
        return ret;
      }
    }
  }
  public partial class CppWinRTGenerator
  {
    public string SpecificationUrl { get; set; }
    public string OpenApiPath { get; set; }
    public string Title { get; set; }
    public string Version { get; set; }

    public List<Path> Paths = new();
    public string ServerUri { get; set; }
    public Dictionary<string, string> Servers = new();

    public Dictionary<string, CppWinRTType> types = new()
    {
      //     { "string", new CppWinRTType{ JsonName = "string", CppWinRTName = "winrt::hstring", IsBuiltIn = true } },
    };

    public string GetBuiltInCppWinRTTypeName(string name)
    {
      switch (name)
      {
        case "string": return "winrt::hstring";
        case "integer": return "int32_t";
        case "boolean": return "bool";
        case "number": return "double";
        default: return string.Empty;
      }
    }

    public CppWinRTType LookupType(string typeName)
    {
      if (types.ContainsKey(typeName)) return types[typeName];
      var cppwinrtType = GetBuiltInCppWinRTTypeName(typeName);
      if (cppwinrtType != string.Empty)
      {
        var type = new CppWinRTType
        {
          JsonName = typeName,
          CppWinRTName = cppwinrtType,
          IsBuiltIn = true
        };
        types[typeName] = type;

        return type;
      }
      return null;
    }

    public string GetCppWinRTParameters(Path path, bool withHttpClient)
    {
      StringBuilder sb = new();
      var paramDefs = path.Parameters.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTFullName} {p.Name}");
      if (path.RequestBody != null)
      {
        var bodyParams = path.RequestBody.Properties.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTFullName} {p.Name}");
        paramDefs = paramDefs.Concat(bodyParams);
      }
      if (withHttpClient) paramDefs = new string[] { "THttpClient _client" }.Concat(paramDefs);
      var defs = string.Join(", ", paramDefs);
      return defs;
    }

    public string ConstructPath(Path path)
    {
      // replace the /{foo...}/{bar...}/... from the path template with "/{}/{}/...,  foo, bar, ..."
      var template = path.PathUriTemplate;
      Regex regex = new Regex(@"{([^}])*}");
      // extract the value enclosed by braces from the regex matches
      var pathVariables = regex.Matches(template).Select(m => m.Value[1..^1]);
      var pathTemplate = regex.Replace(template, "{}");
      var comma = pathVariables.Count() > 0 ? ", " : string.Empty;
      return $"LR\"({ServerUri}{pathTemplate})\"{comma}{string.Join(", ", pathVariables)}";
    }
    struct GetSetName
    {
      public string Get { get; set; }
      public string Set { get; set; }
    }

    Dictionary<string, GetSetName> ValueMethodNames = new()
    {
      { "string", new GetSetName{ Get = "GetNamedString", Set = "CreateStringValue" } },
      { "integer", new GetSetName { Get = "GetNamedNumber", Set = "CreateNumberValue" } },
      { "number", new GetSetName { Get = "GetNamedNumber", Set = "CreateNumberValue" } },
      { "boolean", new GetSetName { Get = "GetNamedBoolean", Set = "CreateBooleanValue" } },
    };
    public string CreateValueMethodName(Parameter p)
    {
      var jsonType = p.Schema.JsonName;
      if (ValueMethodNames.ContainsKey(jsonType)) return $"winrt::Windows::Data::Json::JsonValue::{ValueMethodNames[jsonType].Set}";
      else return $"{p.Schema.CppWinRTFullName}::ToJsonValue";
    }

    public string JsonObjectMethod(Parameter property)
    {
      var cppwinrtType = property.Schema.JsonName;
      if (ValueMethodNames.ContainsKey(cppwinrtType)) return ValueMethodNames[cppwinrtType].Get;
      else return string.Empty;
    }


    public string ConstructJsonRequestPayload(Path path)
    {
      var parameters = path.RequestBody?.Properties.Select(p => $"\"{p.Name}\": {{}}");
      if (parameters == null || parameters.Count() == 0) return "{{ }}";
      var template = string.Join(",\n", parameters);
      var variables = string.Join(", ", path.RequestBody.Properties.Select(p => p.Name));
      
      return $"LR\"{{\n{template}\n}}\", {variables}";
    }

    internal CppWinRTType CreateType(string @namespace, string typeName, JsonObject typeDef)
    {
      var type = new CppWinRTType
      {
        JsonName = typeName,
        CppWinRTName = typeName,
        IsBuiltIn = false,
        Namespace = @namespace,
      };
      type.Members.AddRange(typeDef.Select(p => new Parameter
      {
        Name = p.Key,
        // TODO: openapi schema may not have type definition
        Schema = LookupType(/*p.Value["type"]!.ToString()!*/ "string"),
      }));
      types[typeName] = type;
      return type;
    }
  }
}

public static class StringExtensions
{
  public static string ToCamelCase(this string methodName)
  {
    return methodName.ToUpperInvariant()[0] + methodName.Substring(1);
  }
}