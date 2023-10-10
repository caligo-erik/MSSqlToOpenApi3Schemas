# Caligo.SqlToOpenApi3Schemas version {PACKAGE_VERSION}

This is a command-line tool that extracts schema information from a Microsoft SQL Server database and generates the schemas.yaml to be used in an OpenAPI 3 specification.

## Installation

You can install Caligo.SqlToOpenApi3Schemas as a global CLI tool using [NuGet](https://www.nuget.org/). Open a command prompt or terminal and run the following command:

```bash
dotnet tool install --global Caligo.SqlToOpenApi3Schemas --version {PACKAGE_VERSION}
```

## Usage
Usage
Once Caligo.SqlToOpenApi3Schemas is installed, you can use it from the command line as follows:

```bash
SqlToOpenApi3Schemas -c your-connection-string
```
## Optional parameters

You can specify a specific database schema, and the extractor will only extract the schema information for the specified schema:
```bash
SqlToOpenApi3Schemas -c your-connection-string -s your-schema
```
If no schema is specified, the default schema assigned to the login will be used, typically dbo.

You can also specify the output path for the schemas.yaml file:
```bash
SqlToOpenApi3Schemas -c your-connection-string -o outputPath
```