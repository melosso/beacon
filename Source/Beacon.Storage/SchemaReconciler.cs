using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace Beacon.Storage;

public static class SchemaReconciler
{
    public static int Apply(BeaconDbContext db)
    {
        var existing = ReadColumns(db);
        var target = db.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var create = db.GetService<IMigrationsModelDiffer>().GetDifferences(null, target);

        var operations = new List<MigrationOperation>();
        var newColumns = new HashSet<(string Table, string Column)>();

        foreach (var table in create.OfType<CreateTableOperation>())
        {
            if (!existing.TryGetValue(table.Name, out var columns))
            {
                operations.Add(table);
                continue;
            }

            foreach (var column in table.Columns.Where(c => !columns.Contains(c.Name)))
            {
                operations.Add(ToAddColumn(column));
                newColumns.Add((table.Name, column.Name));
            }
        }

        var createdTables = operations.OfType<CreateTableOperation>().Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        operations.AddRange(create.OfType<CreateIndexOperation>().Where(index =>
            createdTables.Contains(index.Table) || index.Columns.All(c => newColumns.Contains((index.Table, c)))));

        if (operations.Count == 0)
            return 0;

        var commands = db.GetService<IMigrationsSqlGenerator>().Generate(operations, db.Model);
        foreach (var command in commands)
        {
#pragma warning disable EF1002
            db.Database.ExecuteSqlRaw(command.CommandText);
#pragma warning restore EF1002
        }
        return operations.Count;
    }

    private static AddColumnOperation ToAddColumn(AddColumnOperation column)
    {
        var add = new AddColumnOperation
        {
            Name = column.Name,
            Table = column.Table,
            Schema = column.Schema,
            ClrType = column.ClrType,
            ColumnType = column.ColumnType,
            IsUnicode = column.IsUnicode,
            IsFixedLength = column.IsFixedLength,
            MaxLength = column.MaxLength,
            Precision = column.Precision,
            Scale = column.Scale,
            IsNullable = column.IsNullable,
            DefaultValue = column.DefaultValue,
            DefaultValueSql = column.DefaultValueSql,
            ComputedColumnSql = column.ComputedColumnSql,
            Collation = column.Collation
        };
        add.AddAnnotations(column.GetAnnotations());

        if (!add.IsNullable && add.DefaultValue is null && add.DefaultValueSql is null && add.ComputedColumnSql is null)
            add.DefaultValue = add.ClrType == typeof(string) ? string.Empty
                : add.ClrType.IsValueType ? Activator.CreateInstance(add.ClrType) : null;

        return add;
    }

    private static Dictionary<string, HashSet<string>> ReadColumns(BeaconDbContext db)
    {
        var provider = db.Database.ProviderName ?? "";
        var schema = provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ? "SCHEMA_NAME()"
            : provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "current_schema()"
            : "DATABASE()";

        var tables = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) connection.Open();
        try
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = $"SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = {schema}";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var table = reader.GetString(0);
                if (!tables.TryGetValue(table, out var columns))
                    tables[table] = columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                columns.Add(reader.GetString(1));
            }
        }
        finally
        {
            if (opened) connection.Close();
        }
        return tables;
    }
}
