namespace ANX.Tools.SchemaDump;

using System.Globalization;
using System.Text;
using ANX.Core.Methods;
using ANX.Method.Bending;
using ANX.Storage;

/// <summary>
/// 把各方法声明的表定义渲染成**带字段含义注释**的建表 SQL（审核 / 存档用）。
/// 用法：`anx-schema-dump [--out &lt;文件路径&gt;]`；不带 --out 时只打印到控制台。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        IMethodPersistenceModule[] methods = [new BendingPersistenceModule()];

        var sql = new StringBuilder();
        sql.AppendLine("-- ANX 建表 SQL：由 ANX.Tools.SchemaDump 从表定义生成，请勿手改");
        sql.AppendLine(
            $"-- 架构版本：{SchemaBuilder.CurrentSchemaVersion}（库版本对不上时程序报错并要求备份后重建）"
        );
        sql.AppendLine(
            $"-- 生成时间：{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}"
        );
        sql.AppendLine();

        sql.AppendLine("-- ==================== 框架表 ====================");
        foreach (var statement in SchemaBuilder.BuildFrameworkTables())
        {
            sql.AppendLine(statement);
            sql.AppendLine();
        }

        foreach (var method in methods)
        {
            sql.AppendLine($"-- ==================== 方法 {method.MethodCode} ====================");
            sql.AppendLine();

            foreach (var table in new[] { method.MainTable, method.DetailTable, method.TemplateTable })
            {
                sql.AppendLine(SchemaBuilder.BuildTable(table));
                sql.AppendLine();
            }
        }

        var text = sql.ToString();
        Console.WriteLine(text);

        var outPath = GetArg(args, "--out");
        if (outPath is null)
        {
            return 0;
        }

        var fullPath = Path.GetFullPath(outPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"已写入：{fullPath}");
        return 0;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
