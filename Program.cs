﻿using CommandLine;
using Dapper;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;
using YamlDotNet.Serialization;

namespace Caligo.SqlToOpenApi3Schemas
{

  enum ExitCode : int
  {
    Success = 0,
    MissingArguments = 1,
    SqlConnectionError = 2,
    IOError = 4
  }

  class Options
  {
    [Option('c', "connection", Required = true, HelpText = "Database connection string.")]
    public string ConnectionString { get; set; }

    [Option('s', "schema", HelpText = "Database schema name. Required only to filter for a specific schema. If not specified, the default schema assigned to the DB login will be used")]
    public string Schema { get; set; }

    [Option('o', "output", Default = "schemas.yaml", HelpText = "Output file path.")]
    public string OutputPath { get; set; }

    [Option('p', "paths", HelpText = "paths file name. If this parameter or pathPrefix are specified, the tool will generate the OpenAPI 3.0 paths to access the tables.")]
    public string PathsFileName { get; set; }

    [Option("pathPrefix", HelpText = "prefix to be added to the path, e.g. /v1/data. If this parameter or pathPrefix are specified, the tool will generate the OpenAPI 3.0 paths to access the tables.")]
    public string PathPrefix { get; set; }
  }

  class CrudPath
  {
    public const string pathTemplate = @"[PATH_PREFIX]/[SCHEMA_NAME]:
  get:
    security:
      - bearerAuth: []
    tags:
      - CRUDs
    responses:
      '200':
        description: [SCHEMA_NAME]
        content:
          application/json:
            schema:
              properties:
                success:
                  type: array
                  items:
                    $ref: ./schemas/schemas.yaml#/[SCHEMA_NAME]
                error:
                  $ref: ./schemas/subschemas.yaml#/Error
  post:
    security:
      - bearerAuth: []
    tags:
      - CRUDs
    requestBody:
      content:
        application/json:
          schema:
            items:
              $ref: ./schemas/schemas.yaml#/[SCHEMA_NAME]
    responses:
      '200':
        description: [SCHEMA_NAME]
        content:
          application/json:
            schema:
              properties:
                success:
                  $ref: ./schemas/schemas.yaml#/[SCHEMA_NAME]
                error:
                  $ref: ./schemas/subschemas.yaml#/Error
  delete:
    security:
      - bearerAuth: []
    tags:
      - CRUDs
    requestBody:
      content:
        application/json:
          schema:
            items:
              $ref: ./schemas/schemas.yaml#/[SCHEMA_NAME]
    responses:
      '200':
        description: [SCHEMA_NAME]
        content:
          application/json:
            schema:
              properties:
                success:
                  $ref: ./schemas/schemas.yaml#/[SCHEMA_NAME]
                error:
                  $ref: ./schemas/subschemas.yaml#/Error";

    public static string GetPaths(List<string> tableNames, string pathPrefix)
    {
      StringBuilder sb = new StringBuilder();

      foreach (var tableName in tableNames)
      {
        sb.AppendLine(pathTemplate.Replace("[SCHEMA_NAME]", tableName).Replace("[PATH_PREFIX]", pathPrefix));
      }
      return sb.ToString();
    }
  }

  class Program
  {

    static void Main(string[] args)
    {
      Parser.Default.ParseArguments<Options>(args)
      .WithParsed(options =>
      {
        string connectionString = options.ConnectionString;
        string schema = options.Schema;
        string outputPath = options.OutputPath;
        string pathsFileName = options.PathsFileName;
        string pathPrefix = options.PathPrefix;

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
          GenerateSchemas(connectionString, schema, outputPath, pathsFileName, pathPrefix);
          Environment.Exit((int)ExitCode.Success);
        }
        else
        {
          Environment.Exit((int)ExitCode.MissingArguments);
        }
      });
    }

    private static void GenerateSchemas(string connectionString, string schemaName, string outputPath, string pathsFileName, string pathPrefix)
    {
      try
      {
        using SqlConnection connection = new SqlConnection(connectionString);
        connection.Open();
        var schemaCheck = !string.IsNullOrWhiteSpace(schemaName) ? $"and t.TABLE_SCHEMA = '{schemaName}' " : string.Empty;

        var columns = connection.Query<Column>(@"SELECT t.TABLE_NAME, c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.CHARACTER_MAXIMUM_LENGTH, c.NUMERIC_PRECISION, c.NUMERIC_SCALE FROM INFORMATION_SCHEMA.TABLES t 
	inner join INFORMATION_SCHEMA.COLUMNS c on t.TABLE_NAME = c.TABLE_NAME
	WHERE t.TABLE_TYPE = 'BASE TABLE' " + schemaCheck + @"
	order by t.TABLE_NAME, c.COLUMN_NAME");
        var primaryKeys = connection.Query<dynamic>(@$"SELECT 
                k.TABLE_NAME, k.COLUMN_NAME
                FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE k inner join INFORMATION_SCHEMA.TABLES t on k.TABLE_NAME = t.TABLE_NAME
                WHERE t.TABLE_TYPE = 'BASE TABLE' " + schemaCheck + @" AND OBJECTPROPERTY(OBJECT_ID(CONSTRAINT_SCHEMA + '.' + CONSTRAINT_NAME), 'IsPrimaryKey') = 1;");

        Dictionary<string, Schema> schemas = new Dictionary<string, Schema>();

        if (columns != null && columns.Count() == 0)
        {
          Console.WriteLine("No tables found.");
          return;
        }
        foreach (var column in columns)
        {
          var tableName = column.TABLE_NAME;

          if (!schemas.ContainsKey(tableName))
          {
            Console.WriteLine($"Table Name: {tableName}");

            schemas.Add(tableName, new Schema
            {
              Name = tableName
            });
          }
          var schema = schemas[tableName];

          schema.Properties.Add(column.COLUMN_NAME, GetProperty(column));
        }
        foreach (var key in primaryKeys)
        {
          var schema = schemas[key.TABLE_NAME];
          schema.PrimaryKeys.Add(key.COLUMN_NAME);
        }
        foreach (var schema in schemas.Values) {
          if (schema.PrimaryKeys.Count == schema.Properties.Count) {
            // all properties are primary keys -> weak table
            schema.IsWeakSchema = true;
          }
        }
        
        CreateSchemaYaml(outputPath, schemas);

        if (!string.IsNullOrWhiteSpace(pathsFileName) || !string.IsNullOrWhiteSpace(pathPrefix))
        {
          var paths = CrudPath.GetPaths(schemas.Keys.ToList(), pathPrefix);
          CreatePathsYaml(pathsFileName, paths);
        }
      }
      catch (System.Exception ex)
      {
        Console.WriteLine($"An error occurred: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        Environment.Exit((int)ExitCode.SqlConnectionError);
      }
    }

    private static void CreateSchemaYaml(string outputPath, Dictionary<string, Schema> schemas)
    {
      try
      {
        var serializer = new SerializerBuilder()
          .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
          .Build();
        var yaml = serializer.Serialize(schemas);
        // Specify the file path
        string filePath = !string.IsNullOrEmpty(outputPath) ? outputPath : "schemas.yaml";

        Console.WriteLine($"Writing to file: {filePath}");
        // Write the YAML string to the file
        File.WriteAllText(filePath, yaml);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        Environment.Exit((int)ExitCode.IOError);
      }
    }

    private static void CreatePathsYaml(string pathsFileName, string paths)
    {
      try
      {
        string filePath = !string.IsNullOrEmpty(pathsFileName) ? pathsFileName : "paths.yaml";
        File.WriteAllText(filePath, paths);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"An error occurred: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        Environment.Exit((int)ExitCode.IOError);
      }
    }

    private static Property GetProperty(Column column)
    {
      var property = new Property
      {
        Name = column.COLUMN_NAME,
        DBType = column.DATA_TYPE,
        IsNullable = column.IS_NULLABLE == "YES",
      };
      switch (column.DATA_TYPE)
      {
        case "bigint":
          {
            property.CSharpType = "long";
            property.OpenApiType = "integer";
            property.OpenApiFormat = "int64";
            break;
          }
        case "bit":
          {
            property.CSharpType = "bool";
            property.OpenApiType = "boolean";
            break;
          }
        case "float":
          {
            property.CSharpType = "double";
            property.OpenApiType = "number";
            property.OpenApiFormat = "double";

            break;
          }
        case "decimal":
        case "money":
        case "numeric":
        case "smallmoney":
          {
            property.CSharpType = "decimal";
            property.OpenApiType = "number";
            property.NumericPrecision = column.NUMERIC_PRECISION;
            property.NumericScale = column.NUMERIC_SCALE;
            break;
          }
        case "real":
          {
            property.CSharpType = "Single";
            property.OpenApiType = "number";
            property.OpenApiFormat = "single";
            property.NumericPrecision = column.NUMERIC_PRECISION;
            property.NumericScale = column.NUMERIC_SCALE;
            break;
          }
        case "smallint":
          {
            property.CSharpType = "short";
            property.OpenApiType = "integer";
            property.OpenApiFormat = "int16";
            break;
          }
        case "int":
          {
            property.CSharpType = "int";
            property.OpenApiType = "integer";
            property.OpenApiFormat = "int32";
            break;
          }
        case "tinyint":
          {
            property.CSharpType = "byte";
            property.OpenApiType = "integer";
            property.OpenApiFormat = "int32";
            break;
          }

        case "date":
        case "datetime":
        case "datetime2":
          {
            property.CSharpType = "DateTime";
            property.OpenApiType = "string";
            property.OpenApiFormat = "date-time";
            break;
          }
        case "time":
          {
            property.CSharpType = "TimeSpan";
            property.OpenApiType = "string";
            property.OpenApiFormat = "time-span";
            break;
          }
        case "char":
        case "nchar":
        case "nvarchar":
        case "varchar":
          {
            property.CSharpType = "string";
            property.OpenApiType = "string";
            property.MaxLength = column.CHARACTER_MAXIMUM_LENGTH;
            break;
          }
        case "uniqueidentifier":
          {
            property.CSharpType = "Guid";
            property.OpenApiType = "string";
            property.OpenApiFormat = "uuid";
            break;
          }
        default:
          {
            throw new Exception($"Unsupported data type: {column.DATA_TYPE} in table {column.TABLE_NAME} column {column.COLUMN_NAME}");
          }
      }
      return property;
    }
  }

  internal class Property
  {
    [YamlMember(Alias = "x-db-type")]
    public string? DBType = null;
    [YamlMember(Alias = "x-csharp-type")]
    public string CSharpType;
    [YamlMember(Alias = "type")]
    public string OpenApiType;
    [YamlMember(Alias = "format")]
    public string OpenApiFormat;

    [YamlIgnore]
    public string Name;
    [YamlMember(Alias = "x-db-nullable")]
    public bool IsNullable;
    [YamlMember(Alias = "x-db-length")]
    public int? MaxLength;
    [YamlMember(Alias = "x-db-precision")]
    public int? NumericPrecision;
    [YamlMember(Alias = "x-db-scale")]
    public int? NumericScale;
  }

  internal class Schema
  {
    [YamlMember(Alias = "x-schemaname")]
    public string? Name;
    [YamlMember(Alias = "properties")]
    public Dictionary<string, Property> Properties = new Dictionary<string, Property>();
    [YamlMember(Alias = "x-db-keys")]
    public List<string> PrimaryKeys = new List<string>();

    [YamlMember(Alias = "x-weakschema")]
    public bool? IsWeakSchema;
  }

  internal class Column
  {
    public string? TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE;
    public int? CHARACTER_MAXIMUM_LENGTH, NUMERIC_PRECISION, NUMERIC_SCALE;
  }

}