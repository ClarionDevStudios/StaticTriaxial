namespace STS.Method.ShearStrength.Tests;

using STS.Storage;
using Microsoft.Data.Sqlite;

/// <summary>
/// 测试用临时 SQLite 库：建库 / 装配 <see cref="TestSheetService"/> / 用完清理
/// （与 STS.Storage.Tests 的建库方式同源；方法工程的读写测试都走它）。
/// </summary>
internal sealed class TempSheetDatabase : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"sts-shear-{Guid.NewGuid():N}.db"
    );

    public TestSheetService CreateService()
    {
        var module = new ShearStrengthPersistenceModule();
        var initializer = new DatabaseInitializer(new StorageOptions(_databasePath), [module]);
        initializer.Initialize();

        return new TestSheetService(initializer, new SheetNumberGenerator(initializer), [module]);
    }

    public ShearStrengthExecution CreateExecution() => new(CreateService());

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
