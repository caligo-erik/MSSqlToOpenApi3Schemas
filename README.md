# Caligo.SqlToOpenApi3Schemas

This is a command-line tool that extracts schema information from a Microsoft SQL Server database and generates the schemas.yaml to be used in an OpenAPI 3 specification.

## Usage

Once Caligo.SqlToOpenApi3Schemas is installed, you can use it from the command line as follows:

```bash
SqlToOpenApi3Schemas -c "your-connection-string"
```

## Optional parameters

### Database schema

You can specify a specific database schema, and the extractor will only extract the schema information for the specified schema:
```bash
SqlToOpenApi3Schemas -c "your-connection-string" -s your-schema
```
If no schema is specified, the default schema assigned to the login will be used, typically dbo.

### Output path

You can also specify the output path for the schemas.yaml file:
```bash
SqlToOpenApi3Schemas -c "your-connection-string" -o outputPath
```

### CRUD paths

You can specify the fileName and prefix for the crud paths
```bash
SqlToOpenApi3Schemas -c "your-connection-string" --pathPrefix /api/data --paths pathsFileName
```