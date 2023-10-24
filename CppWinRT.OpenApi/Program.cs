using CppWinRT.OpenApi;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var jsonText = File.ReadAllText(@"F:\CppWinRTBuilders\CppWinRT.OpenApi\example.json");
var json = JsonSerializer.Deserialize<JsonObject>(jsonText)!;

var title = json["info"]!["title"]!.ToString();
var version = json["info"]!["version"]!.ToString();

var generator = new CppWinRT.OpenApi.CppWinRTGenerator
{
  Title = title,
  Version = version
};

// select a server
var servers = json["servers"]!.AsArray();
foreach (var server in servers)
{
  var url = server!["url"]!.ToString();
  var description = server["description"]!.ToString();
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
    var summary = methodValue["summary"]?.ToString();
    var description = methodValue["description"]?.ToString();
    var parameters = methodValue["parameters"]?.AsArray() ?? new JsonArray();
    var requestBody = methodValue["requestBody"]?.AsObject();
    var responses = methodValue["responses"]?.AsObject();
    var tags = methodValue["tags"]?.AsArray();
    var security = methodValue["security"]?.AsArray();
    var pathObject = new CppWinRT.OpenApi.Path
    {
      PathUriTemplate = pathName,
      Method = methodName.ToUpperInvariant()[0]  + methodName.Substring(1), // convert put to Put
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
        pathObject.ResponseType.Add(responseType);
      } else if (mimeType.Value != null)
      {
        var example = mimeType.Value!["example"]!.AsObject();
        var typeDef = example.First().Value.AsObject();
        var typeName = example.First().Key;
        pathObject.ResponseType.Add(generator.CreateType(pathObject.Name, typeName, typeDef));
      }
    }
    generator.Paths.Add(pathObject);
    //var servers = methodValue["servers"]!.AsArray();
  }
}



var output = generator.TransformText();
Console.WriteLine(output);


namespace CppWinRT.OpenApi
{
  public class CppWinRTType
  {
    public string JsonName { get; set; }
    public string CppWinRTName { get; set; }
    public bool IsBuiltIn { get; set; }
    public List<Parameter> Members { get; set; } = new();
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

    public Request RequestBody;

    public List<CppWinRTType> ResponseType = new();
  }
  public partial class CppWinRTGenerator
  {
    public string Title { get; set; }
    public string Version { get; set; }

    public List<Path> Paths = new();
    public string ServerUri { get; set; }
    public Dictionary<string, string> Servers = new();

    public Dictionary<string, CppWinRTType> types = new()
    {
      //     { "string", new CppWinRTType{ JsonName = "string", CppWinRTName = "winrt::hstring", IsBuiltIn = true } },
    };

    public string GetCppWinRTTypeName(string name)
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
      var cppwinrtType = GetCppWinRTTypeName(typeName);
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

    public string GetCppWinRTParameters(Path path)
    {
      StringBuilder sb = new();
      var paramDefs = path.Parameters.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTName} {p.Name}");
      if (path.RequestBody != null)
      {
        var bodyParams = path.RequestBody.Properties.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTName} {p.Name}");
        paramDefs = paramDefs.Concat(bodyParams);
      }

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
      return $"LR\"({ServerUri}{pathTemplate})\", {string.Join(", ", pathVariables)}";
    }

    public string CreateValueMethodName(Parameter p)
    {
      var cppwinrtType = p.Schema.JsonName;
      switch (cppwinrtType)
      {
        case "string": return "CreateStringValue";
        case "number": return "CreateNumberValue";
        case "integer": return "CreateNumberValue";
        case "boolean": return "CreateBooleanValue";

        default: return string.Empty;
      }
    }

    public string ConstructJsonRequestPayload(Path path)
    {
      var parameters = path.RequestBody?.Properties.Select(p => $"\"{p.Name}\": {{}}");
      if (parameters == null || parameters.Count() == 0) return "{{ }}";
      var template = string.Join(",\n", parameters);
      var variables = string.Join(", ", path.RequestBody.Properties.Select(p => p.Name));
      
      return $"LR\"{{\n{template}\n}}\", {variables}";
    }

    internal CppWinRTType CreateType(string pathName, string typeName, JsonObject typeDef)
    {
      var type = new CppWinRTType
      {
        JsonName = typeName,
        CppWinRTName = $"winrt::{pathName}_{typeName}",
        IsBuiltIn = false
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