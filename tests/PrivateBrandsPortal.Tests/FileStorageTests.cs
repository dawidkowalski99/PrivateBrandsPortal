using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PrivateBrandsPortal.Web.Configuration;
using PrivateBrandsPortal.Web.Services;
namespace PrivateBrandsPortal.Tests;

public sealed class FileStorageTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"PortalStorageTests",Guid.NewGuid().ToString("N"));
    internal FileStorage Storage {get;}
    public FileStorageTests(){Directory.CreateDirectory(root);Storage=new(Options.Create(new FileStorageOptions{RootPath=root}),NullLogger<FileStorage>.Instance);}
    private static string Key()=> $"projects/1/{Guid.NewGuid():N}.xlsx";
    [Theory][InlineData(".xlsx")][InlineData(".pdf")][InlineData(".txt")][InlineData(".doc")][InlineData(".docx")] public async Task Roundtrip_preserves_bytes_hash_and_prevents_overwrite(string extension)
    {
        var key=$"projects/1/{Guid.NewGuid():N}{extension}";byte[] bytes=[0,255,3,10,99];
        var stored=await Storage.SaveAsync(key,new MemoryStream(bytes),10,default);
        Assert.Equal(bytes.Length,stored.Size);Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),stored.Sha256);
        await Assert.ThrowsAsync<IOException>(()=>Storage.SaveAsync(key,new MemoryStream([1]),10,default));
        await using(var read=await Storage.OpenAsync(key,default)){using var copy=new MemoryStream();await read.CopyToAsync(copy);Assert.Equal(bytes,copy.ToArray());}
        await Storage.DeleteAsync(key,default);await Assert.ThrowsAsync<FileNotFoundException>(()=>Storage.OpenAsync(key,default));
    }
    [Theory][InlineData("../escape.xlsx")][InlineData("projects/1/../../escape.xlsx")][InlineData("C:/escape.xlsx")][InlineData("projects/1/test.xlsx")][InlineData("projects/1/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.xlsx:stream")]
    public async Task Keys_cannot_traverse_or_use_user_filenames(string key)=>
        await Assert.ThrowsAsync<ValidationException>(()=>Storage.SaveAsync(key,new MemoryStream([1]),10,default));
    [Theory][InlineData(0)][InlineData(11)]
    public async Task Empty_or_oversized_stream_is_rejected_and_partial_file_removed(int size)
    {
        var key=Key();await Assert.ThrowsAsync<ValidationException>(()=>Storage.SaveAsync(key,new MemoryStream(new byte[size]),10,default));
        Assert.Empty(Directory.GetFiles(root,"*",SearchOption.AllDirectories));
    }
    [Fact] public async Task Cleanup_removes_only_old_temp_files_not_project_files()
    {
        var old=$"temp/1/{Guid.NewGuid():N}/{Guid.NewGuid():N}.xls";var recent=$"temp/1/{Guid.NewGuid():N}/{Guid.NewGuid():N}.xls";var permanent=Key();
        foreach(var key in new[]{old,recent,permanent})await Storage.SaveAsync(key,new MemoryStream([1]),10,default);
        File.SetLastWriteTimeUtc(Path.Combine(root,old),DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(root,permanent),DateTime.UtcNow.AddDays(-2));
        Assert.Equal(1,await Storage.CleanupTemporaryAsync(DateTimeOffset.UtcNow.AddHours(-24),default));
        Assert.Equal(2,Directory.GetFiles(root,"*",SearchOption.AllDirectories).Length);
    }
    [Fact] public async Task Missing_root_is_not_silently_created()
    {
        var missing=Path.Combine(root,"missing");var storage=new FileStorage(Options.Create(new FileStorageOptions{RootPath=missing}),NullLogger<FileStorage>.Instance);
        await Assert.ThrowsAsync<IOException>(()=>storage.SaveAsync(Key(),new MemoryStream([1]),10,default));Assert.False(Directory.Exists(missing));
    }
    public void Dispose()=>Directory.Delete(root,true);
}
