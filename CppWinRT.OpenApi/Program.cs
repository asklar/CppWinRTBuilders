using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var outFolder = string.Empty;
var openApiPath = string.Empty;
var preferredServer = string.Empty;

foreach (var arg in args)
{
    if (arg.StartsWith("-o:"))
    {
        outFolder = arg.Substring(3);
    }
    else if (arg.StartsWith("-out:"))
    {
        outFolder = arg.Substring(5);
    }
    else if (arg.StartsWith("-i:"))
    {
        openApiPath = arg.Substring(3);
    }
    else if (arg.StartsWith("-in:"))
    {
        openApiPath = arg.Substring(4);
    }
    else if (arg.StartsWith("-s:"))
    {
        preferredServer = arg.Substring(3);
    }
    else if (arg.StartsWith("-server:"))
    {
        preferredServer = arg.Substring(8);
    }
    else
    {
        throw new ArgumentException($"Unknown argument {arg}");
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
string inputSpec = string.Empty;
string specificationUrl = string.Empty;
if (openApiPath.StartsWith("http://") || openApiPath.StartsWith("https://"))
{
    using (var client = new System.Net.Http.HttpClient())
    {
        inputSpec = await client.GetStringAsync(openApiPath);
        specificationUrl = openApiPath;
    }
}
else
{
    inputSpec = File.ReadAllText(openApiPath);
    specificationUrl = new Uri(openApiPath).AbsoluteUri;
}

// inputSpec is either a json or a yaml. If it is a yaml, convert it to json
if (openApiPath.EndsWith(".yaml") || openApiPath.EndsWith(".yml"))
{
    var yaml = new YamlDotNet.Serialization.DeserializerBuilder().Build();
    var yamlObject = yaml.Deserialize(new StringReader(inputSpec));
    inputSpec = JsonSerializer.Serialize(yamlObject);
}

var json = JsonSerializer.Deserialize<JsonObject>(inputSpec)!;
var title = json["info"]!["title"]!.ToString();
var version = json["info"]!["version"]!.ToString();

var Namespace = title.MakeCppIdentifier();
var generator = new CppWinRT.OpenApi.CppWinRTGenerator
{
    Title = title,
    Version = version,
    SpecificationUrl = specificationUrl,
    OpenApiPath = openApiPath,
    ApiName = Namespace,
};

// select a server
var servers = json["servers"]?.AsArray() ?? new JsonArray();
if (servers.Count() == 0)
{
    var server = new JsonObject();
    server["url"] = json["host"]!.ToString();
    servers.Add(server);
}
foreach (var server in servers)
{
    var url = server!["url"]!.ToString().Trim();
    // if it is a relative url, make it absolute by using the openApiPath as the base
    if (url.StartsWith('/'))
    {
        var uri = new Uri(specificationUrl);
        url = new Uri(uri, url).AbsoluteUri;
    }
    var description = (server["description"]?.ToString() ?? "_NoName").Trim();
    generator.Servers.Add(description, url);
}

if (preferredServer != string.Empty)
{
    generator.ServerUri = generator.Servers[preferredServer];
}
else
{
    var server = generator.Servers.First();
    Console.WriteLine($"Using default server: {server.Key} ({server.Value})");
    Console.WriteLine("Use -s: or -server: to specify a different server");
    generator.ServerDescription = server.Key;
    generator.ServerUri = server.Value;
}

// TODO: the security schemes should only be created as we find them referenced by security entries
// and not hardcode them to be in the components section
var securitySchemes = json["components"]?["securitySchemes"]?.AsObject();
if (securitySchemes != null)
{
    foreach (var scheme in securitySchemes)
    {
        var schemeName = scheme.Key;
        var schemeValue = scheme.Value!.AsObject()!;
        var type = schemeValue["type"]!.ToString();
        if (type == "http")
        {
            var httpScheme = new CppWinRT.OpenApi.HttpSecurityScheme
            {
                Scheme = schemeValue["scheme"]!.ToString(),
                BearerFormat = schemeValue["bearerFormat"]?.ToString() ?? string.Empty,
                Resource = "TODO",
                Type = type,
                Name = schemeName,
            };
            generator.Security[schemeName] = httpScheme;
        }
        else if (type == "apiKey")
        {
            var apiKeyScheme = new CppWinRT.OpenApi.ApiKeySecurityScheme
            {
                Name = schemeValue["name"]!.ToString(),
                In = schemeValue["in"]!.ToString(),
                Type = type,
            };
            generator.Security[schemeName] = apiKeyScheme;
        }
        else if (type == "oauth2")
        {
            var flows = schemeValue["flows"]!.AsObject();
            foreach (var flow in flows)
            {
                var flowName = flow.Key;
                var flowValue = flow.Value!.AsObject()!;
                var oauth2Scheme = new CppWinRT.OpenApi.OAuth2SecurityScheme
                {
                    AuthorizationUrl = flowValue["authorizationUrl"]?.ToString(),
                    TokenUrl = flowValue["tokenUrl"]?.ToString(),
                    Flow = flowName,
                    Type = type,
                    Name = schemeName,
                };
                var scopes = flowValue["scopes"]!.AsObject();
                foreach (var scope in scopes)
                {
                    oauth2Scheme.Scopes[scope.Key] = scope.Value!.ToString();
                }
                generator.Security[schemeName] = oauth2Scheme;
            }
        }
        else if (type == "openIdConnect")
        {
            var openIdConnectScheme = new CppWinRT.OpenApi.OpenIdConnectSecurityScheme
            {
                OpenIdConnectUrl = schemeValue["openIdConnectUrl"]!.ToString(),
                Type = type,
                Name = schemeName,
            };
            generator.Security[schemeName] = openIdConnectScheme;
        }
    }
}

var defaultSecurity = json["security"]?.AsArray();

var paths = json["paths"]!.AsObject();
foreach (var path in paths)
{
    var pathName = path.Key;
    var pathValue = path.Value;
    foreach (var method in pathValue!.AsObject().AsEnumerable())
    {
        var methodName = method.Key;
        var methodValue = method.Value;
        var operationId = methodValue["operationId"]?.ToString();
        var summary = methodValue["summary"]?.ToString().Trim();
        var description = methodValue["description"]?.ToString().Trim();
        var parameters = methodValue["parameters"]?.AsArray() ?? new JsonArray();
        var requestBody = methodValue["requestBody"]?.AsObject();
        var responses = methodValue["responses"]?.AsObject();
        var tags = methodValue["tags"]?.AsArray();
        var securityEntries = methodValue["security"]?.AsArray() ?? defaultSecurity;

        var fnSecuritySchemes = securityEntries?.SelectMany(s => s!.AsObject()!.Select(security => generator.Security[security.Key])).ToArray();
        var pathObject = new CppWinRT.OpenApi.Path
        {
            PathUriTemplate = pathName,
            Method = methodName.ToCamelCase(), // convert put to Put
            OperationId = operationId,
            Summary = summary,
            Description = description,
            Tags = tags?.Select(x => x!.ToString()).ToArray(),
            Security = fnSecuritySchemes,
        };
        foreach (var iparameter in parameters)
        {
            var parameter = iparameter;
            var paramObj = parameter!.AsObject();
            if (paramObj.Count == 1 && paramObj.ContainsKey("$ref"))
            {
                var refType = paramObj["$ref"]!.ToString()!;
                parameter = json.ResolveRef(refType);
            }
            var paramName = parameter!["name"]!.ToString();
            var paramIn = parameter["in"]!.ToString();
            var paramDescription = parameter["description"]?.ToString() ?? string.Empty;
            var paramRequired = bool.Parse(parameter["required"]?.ToString() ?? "false");
            var paramSchema = parameter["schema"]?.AsObject();
            if (paramSchema == null)
            {
                // look for the type
                var type = parameter["type"]!.ToString();
                paramSchema = new JsonObject();
                paramSchema["type"] = type;
            }
            var parameterObject = new CppWinRT.OpenApi.Parameter
            {
                Name = paramName,
                In = paramIn,
                Description = paramDescription,
                Required = paramRequired,
                Schema = generator.ResolveType(paramSchema, json),
            };
            pathObject.Parameters.Add(parameterObject);
        }
        if (requestBody != null)
        {
            pathObject.RequestBody = new CppWinRT.OpenApi.Request
            {
                IsRequired = bool.Parse(requestBody!["required"]?.ToString() ?? "false"),
            };
            var content = requestBody["content"]!.AsObject()!;
            var mimeType = content.First();
            var schema = mimeType.Value!["schema"]!.AsObject();
            if (schema.ContainsKey("properties"))
            {
                var properties = schema["properties"]!.AsObject();
                var bodyParam = properties.Select(p => new CppWinRT.OpenApi.Parameter
                {
                    Name = p.Key,
                    Schema = generator.ResolveType(p.Value!.AsObject(), json),
                });
                pathObject.RequestBody.Properties.AddRange(bodyParam);
            }
            else if (schema.ContainsKey("$ref"))
            {
                var type = generator.ResolveType(schema, json);
                var bodyParam = new CppWinRT.OpenApi.Parameter
                {
                    Name = type.JsonName,
                    Schema = type,
                };
                pathObject.RequestBody.Properties.Add(bodyParam);
            }
        }
        if (responses != null)
        {
            var response = responses.First()!;
            // TODO: deal with more than one possible response (as a "union" type)
            var responseEntry = response.Value!.AsObject();
            if (responseEntry.ContainsKey("schema"))
            {
                var schema = responseEntry["schema"]!.AsObject();
                var responseType = generator.ResolveType(schema, json);
                pathObject.ResponseType = responseType;
            }
            else if (responseEntry.ContainsKey("content"))
            {
                var content = responseEntry["content"]!.AsObject()!;
                var mimeType = content.First();
                var schema = mimeType.Value!["schema"]!.AsObject();
                if (schema != null)
                {
                    var responseType = generator.ResolveType(schema, json);
                    pathObject.ResponseType = responseType;
                }
                else if (mimeType.Value != null)
                {
                    if (mimeType.Value.AsObject().ContainsKey("$ref"))
                    {
                        // TODO: deal with references to schema instead of example
                        var refType = mimeType.Value!["$ref"]!.ToString()!;
                        var typeName = refType.Split('/').Last();
                        var jsonNode = json.ResolveRef(refType);
                        var typeDef = jsonNode!.AsObject();
                        pathObject.ResponseType = generator.CreateType(Namespace, typeName, typeDef);
                    }
                    else if (mimeType.Value.AsObject().ContainsKey("example"))
                    {
                        var example = mimeType.Value!["example"]!.AsObject();
                        if (example.Count() != 1) throw new Exception("Only one example is supported");
                        var typeDef = example.First().Value!.AsObject();
                        var typeName = example.First().Key;
                        pathObject.ResponseType = generator.CreateType(Namespace, typeName, typeDef);
                    }
                }
            }
        }
        if (pathObject.ResponseType == null)
        {
            pathObject.ResponseType = generator.ResolveType(null, json);
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
var programDirectory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
File.Copy(System.IO.Path.Combine(programDirectory, "OAuth.h"), System.IO.Path.Combine(outFolder, "winrt", "OAuth.h"), true);

namespace CppWinRT.OpenApi
{
    [DebuggerDisplay("{JsonName} - {CppWinRTName}")]
    public class CppWinRTType
    {
        public string JsonName { get; set; }
        public string CppWinRTFullName
        {
            get =>
              $"{(Namespace != null ? Namespace + "::" : "")}{CppWinRTName}";
        }
        public bool IsBuiltIn { get; set; }
        public List<Parameter> Members { get; set; } = new();
        public string Namespace { get; internal set; }

        public string CppWinRTName { get; set; }
        public bool IsArray { get; internal set; }
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

    [DebuggerDisplay("{PathUriTemplate} - {Method}")]
    public class Path
    {
        public string GetResponseTypeCppWinRTCamelCase()
        {
            return ResponseType.CppWinRTName.ToCamelCase();
        }
        public string PathUriTemplate { get; set; }

        public string PathUriWithoutParameters
        {
            get
            {
                var firstTemplateParameter = PathUriTemplate.IndexOf('{');
                if (firstTemplateParameter == -1) return PathUriTemplate;
                return PathUriTemplate.Substring(0, firstTemplateParameter - 1);
            }
        }
        public string Name
        {
            // given a path, return the name of the method
            get
            {
                var path = PathUriWithoutParameters;
                var parts = path.Split('/');
                var lastPart = parts.Last();
                return lastPart.ToCamelCase();
            }
        }
        public string Method { get; set; }
        //public string OperationId { get; set; }
        public string Summary { get; set; }
        public string Description { get; set; }
        public string[] Tags { get; set; }
        public SecurityScheme[] Security { get; set; }

        public string DefaultSecurityCppType =>
                Security != null && Security.Length > 0 ? Security[0].CppType : "void";
        public List<Parameter> Parameters { get; } = new();

        public string GetParametersNamesWithHttpClient()
        {
            var allParams = new string[] { "_client" }.Concat(Parameters.Select(p => p.Name));
            if (RequestBody != null)
            {
                allParams = allParams.Concat(RequestBody.Properties.Select(p => p.Name));
            }
            if (Security != null && Security.Length > 0)
            {
                allParams = allParams.Concat(new string[] { "_security" });
            }
            return string.Join(", ", allParams);
        }

        public Request RequestBody;

        public CppWinRTType ResponseType = null;

        public string GetCppName(bool withMethod)
        {
            if (withMethod) return $"{Method}{Name.ToCamelCase()}Async";
            return $"{Name.ToCamelCase()}Async";
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

        public string OperationId { get; internal set; }
    }

    public partial class SecuritySchemeGenerator
    {
        private readonly SecurityScheme _scheme;

        public SecuritySchemeGenerator(SecurityScheme scheme)
        {
            _scheme = scheme;
        }
    }

    public class SecurityScheme
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string CppType => Name.MakeCppIdentifier();

        public string GetCppDefinition()
        {
            var generator = new SecuritySchemeGenerator(this);
            return generator.TransformText();
        }
    }

    public class ApiKeySecurityScheme : SecurityScheme
    {
        //public string Name { get; set; } = string.Empty; // TODO: this should match SecurityScheme.Name?
        public string In { get; set; } = string.Empty;
    }

    public class HttpSecurityScheme : SecurityScheme
    {
        public string Scheme { get; set; } = string.Empty;
        public string BearerFormat { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
    }

    public class OAuth2SecurityScheme : SecurityScheme
    {
        public string AuthorizationUrl { get; set; } = string.Empty;
        public string? TokenUrl { get; set; } = string.Empty;
        public string Flow { get; set; } = string.Empty;
        public Dictionary<string, string> Scopes { get; set; } = new();
    }

    public class OpenIdConnectSecurityScheme : SecurityScheme
    {
        public string OpenIdConnectUrl { get; set; } = string.Empty;
    }


    public partial class CppWinRTGenerator
    {
        public string SpecificationUrl { get; set; }
        public string OpenApiPath { get; set; }
        public string Title { get; set; }
        public string Version { get; set; }
        public const string OpenApiNamespace = "winrt::OpenApi";
        public string ApiName { get; set; }

        private string Namespace { get => OpenApiNamespace; }
        public List<Path> Paths = new();
        public string ServerDescription { get; set; }
        public string ServerUri { get; set; }
        public Dictionary<string, string> Servers = new();
        public Dictionary<string, CppWinRTType> types = new();
        public Dictionary<string, SecurityScheme> Security = new();

        public List<KeyValuePair<string, CppWinRTType>> GetGraphOrderedCustomTypes()
        {
            var customTypes = types.Where(t => !t.Value.IsBuiltIn).ToList();
            // build a graph of types, where each type is a node and each member of a non-builtin type is an edge
            var graph = new Dictionary<CppWinRTType, List<CppWinRTType>>();
            foreach (var type in customTypes)
            {
                var members = type.Value.Members.Where(m => !(m.Schema.IsBuiltIn || m.Schema.CppWinRTName == "Void"))
                  .Select(m => m.Schema).ToList();
                graph[type.Value] = members;
            }
            // Now find a walk through the tree that visits each node only once in dependency order:
            // we can visit a node only if either a) it has no dependencies or b) all its dependencies have been visited already
            var visited = new HashSet<CppWinRTType>();
            var result = new List<KeyValuePair<string, CppWinRTType>>();
            while (visited.Count < graph.Count)
            {
                var next = graph.Where(n => !visited.Contains(n.Key) && n.Value.All(v => visited.Contains(v))).FirstOrDefault();
                if (next.Key == null) throw new Exception("Cannot find a walk through the graph");
                visited.Add(next.Key);
                result.Add(customTypes.First(t => t.Value == next.Key));
            }
            return result;
        }

        public string GetCppCast(string jsonType)
        {
            if (builtInTypes.ContainsKey(jsonType)) return builtInTypes[jsonType].CastFromJson ?? string.Empty;
            return string.Empty;
        }

        public CppWinRTType GetArrayElementType(CppWinRTType type)
        {
            if (!type.IsArray) throw new ArgumentException("Type is not an array");
            var arrayType = type.JsonName;
            var elementType = arrayType.Substring("ArrayOf_".Length);
            return LookupType(elementType);
        }
        public CppWinRTType ResolveType(JsonObject node, JsonObject universe)
        {
            if (node == null)
            {
                var type = new CppWinRTType
                {
                    JsonName = "null",
                    CppWinRTName = "JsonObject",
                    Namespace = "winrt::Windows::Data::Json",
                };
                AddType(type);
                return type;
            }
            if (node.ContainsKey("type"))
            {
                var typeName = node["type"]!.ToString()!;
                CppWinRTType type;
                if (typeName == "array")
                {
                    var items = node["items"]!.AsObject();
                    var itemType = ResolveType(items, universe);
                    var jsonName = $"ArrayOf_{itemType.JsonName}";
                    if (types.ContainsKey(jsonName)) return types[jsonName];
                    type = new CppWinRTType
                    {
                        JsonName = jsonName,
                        CppWinRTName = $"ArrayOf_{itemType.CppWinRTName}",
                        IsBuiltIn = false,
                        Namespace = ApiName,
                        IsArray = true,
                    };
                    AddType(type);
                }
                else
                {
                    type = LookupType(typeName);
                }
                return type;
            }
            else if (node.ContainsKey("$ref"))
            {
                var refPath = node["$ref"]!.ToString()!;
                var typeName = refPath.Split('/').Last();
                if (types.ContainsKey(typeName)) return types[typeName];
                var jsonNode = universe.ResolveRef(refPath);
                var typeDef = jsonNode.AsObject();
                var required = typeDef["required"]?.AsArray() ?? new JsonArray();
                var properties = typeDef["properties"]!.AsObject();
                var members = properties.Select(p => new CppWinRT.OpenApi.Parameter
                {
                    Name = p.Key,
                    Schema = ResolveType(p.Value.AsObject(), universe),
                    Required = required.Contains(p.Key),
                }).ToList();
                var type = new CppWinRTType
                {
                    JsonName = typeName,
                    Members = members,
                    CppWinRTName = typeName,
                    IsBuiltIn = false,
                    Namespace = ApiName,
                };
                AddType(type);
                return type;
            }
            throw new Exception("Cannot resolve type");
        }

        private void AddType(CppWinRTType type)
        {
            types[type.JsonName] = type;
        }

        private CppWinRTType LookupType(string typeName)
        {
            if (types.ContainsKey(typeName)) return types[typeName];
            if (builtInTypes.ContainsKey(typeName))
            {
                var cppwinrtType = builtInTypes[typeName];
                var type = new CppWinRTType
                {
                    JsonName = typeName,
                    CppWinRTName = cppwinrtType.CppName,
                    IsBuiltIn = true,
                    Namespace = cppwinrtType.Namespace,
                };
                AddType(type);

                return type;
            }
            return null;
        }

        public string GetCppWinRTParameters(Path path)
        {
            StringBuilder sb = new();
            var paramDefs = path.Parameters.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTFullName} {p.Name}");
            if (path.RequestBody != null)
            {
                var bodyParams = path.RequestBody.Properties.Select(p => $"{LookupType(p.Schema.JsonName).CppWinRTFullName} {p.Name}");
                paramDefs = paramDefs.Concat(bodyParams);
            }

            if (path.Security != null && path.Security.Length > 0)
            {
                //Debug.Assert(path.Security.Length == 1);
                var securityParams = path.Security.Select(s => "const TSecuritySchema& _security = {}");
                var one = new string[] { securityParams.First() };
                paramDefs = paramDefs.Concat(one);
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

            var queryVariables = path.Parameters.Where(p => p.In == "query").Select(p => p.Name);
            var queryTemplate = string.Join("&", queryVariables.Select(v => $"{v}={{}}"));
            if (queryTemplate != string.Empty) queryTemplate = "?" + queryTemplate;

            var allVariables = pathVariables.Concat(queryVariables);
            var std_format_variables = allVariables.Select(v =>
            {
                var schema = path.Parameters.Where(p => v == p.Name).Select(p => p.Schema);
                Debug.Assert(schema.Count() == 1);
                if (schema.First().JsonName == "string") return $"winrt::Windows::Foundation::Uri::EscapeComponent({v})";
                else return v;
            });
            var list_of_variables = string.Join(", ", std_format_variables);
            var comma = std_format_variables.Count() > 0 ? ", " : string.Empty;

            return $"LR\"({{}}{pathTemplate}{queryTemplate})\", serverUri{comma}{list_of_variables}";
        }
        struct BuiltInType
        {
            public string Get { get; set; }
            public string Set { get; set; }
            public string CppName { get; set; }
            public string CastFromJson { get; set; }
            public string Namespace { get; set; }
            public string FromJson { get; set; }
        }

        Dictionary<string, BuiltInType> builtInTypes = new()
    {
      { "string", new BuiltInType{ Get = "GetNamedString", Set = "CreateStringValue", CppName="wstring", Namespace="std", FromJson="GetString" } },
      { "integer", new BuiltInType { Get = "GetNamedNumber", Set = "CreateNumberValue", CppName = "int32_t", CastFromJson = "static_cast<int32_t>", FromJson = "GetNumber" } },
      { "number", new BuiltInType { Get = "GetNamedNumber", Set = "CreateNumberValue", CppName = "double", FromJson = "GetNumber" } },
      { "boolean", new BuiltInType { Get = "GetNamedBoolean", Set = "CreateBooleanValue", CppName= "bool", FromJson = "GetBoolean" } },
    };

        public string CreateValueMethodName(CppWinRTType p)
        {
            var jsonType = p.JsonName;
            if (builtInTypes.ContainsKey(jsonType)) return $"winrt::Windows::Data::Json::JsonValue::{builtInTypes[jsonType].Set}";
            else return $"{p.CppWinRTFullName}::ToJsonValue";
        }

        public string GetFromJsonMethodName(CppWinRTType type)
        {
            if (builtInTypes.ContainsKey(type.JsonName)) return builtInTypes[type.JsonName].FromJson;
            else return "GetObject";
        }
        public string JsonObjectMethod(Parameter property)
        {
            var cppwinrtType = property.Schema.JsonName;
            if (builtInTypes.ContainsKey(cppwinrtType)) return builtInTypes[cppwinrtType].Get;
            else if (property.Schema.IsArray) return "GetNamedArray";
            else return "GetNamedObject";
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
            AddType(type);
            return type;
        }

        string GetPathCppName(Path path)
        {
            var name = string.Empty;

            if (path.OperationId != null)
            {
                name = path.OperationId.ToCamelCase() + "Async";
            }
            else
            {
                var cppName = path.GetCppName(false);
                var conflicts = Paths.Where(p => p != path && p.GetCppName(false) == cppName);
                if (conflicts.Any())
                {
                    // either we have multiple HTTP methods with the same operationId,
                    // or we have multiple paths with the same last segment
                    var pathWithoutParameters = path.PathUriWithoutParameters;
                    name = pathWithoutParameters.Split('/')
                        .Skip(1)
                        .Select(x => x.MakeCppIdentifier()).Aggregate((x, y) => x + y) + "Async";
                }
                else
                {
                    name = cppName;
                }
            }
            return name;
        }

    }
}

public static class Extensions
{
    public static JsonObject ResolveRef(this JsonObject json, string refType)
    {
        var refPath = refType.Split('/');
        Debug.Assert(refPath[0] == "#");
        var jsonNode = json;
        foreach (var pathPart in refPath.Skip(1))
        {
            jsonNode = jsonNode[pathPart]!.AsObject();
        }

        return jsonNode;
    }
    public static string ToCamelCase(this string methodName)
    {
        return methodName.ToUpperInvariant()[0] + methodName.Substring(1);
    }

    public static string MakeCppIdentifier(this string text)
    {
        var validCharsInCppIdentifiers = new Regex(@"[A-Za-z0-9_]");
        // traverse the text, whenever an invalid character is found, remove it and make the next character uppercase
        var sb = new StringBuilder();
        var firstChar = true;
        foreach (var c in text)
        {
            if (validCharsInCppIdentifiers.IsMatch(c.ToString()))
            {
                if (firstChar)
                {
                    sb.Append(c.ToString().ToUpperInvariant());
                    firstChar = false;
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                firstChar = true;
            }
        }
        return sb.ToString();
    }
}


