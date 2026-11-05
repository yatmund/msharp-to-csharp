using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Platform.Entities
{
    public interface IBlobStorageProvider
    {
        Task SaveAsync(Blob blob);
        Task DeleteAsync(Blob blob);
        Task<byte[]> LoadAsync(Blob blob);
        Task<bool> FileExistsAsync(Blob blob);
        bool CostsToCheckExistence();
    }

    public class BlobStorageProviderFactory
    {
        public static Dictionary<string, IBlobStorageProvider> Providers = new Dictionary<string, IBlobStorageProvider>();

        internal static IBlobStorageProvider Default = new DiskBlobStorageProvider();

        internal static IBlobStorageProvider Get(Blob blob) =>
            blob.FolderName != null && Providers.TryGetValue(blob.FolderName, out var provider) ? provider : Default;
    }

    /// <summary>Stores files under Config "Blob:RootPath" (default "App_Data/Files"), in a folder per owner type and property.</summary>
    public class DiskBlobStorageProvider : IBlobStorageProvider
    {
        public static DirectoryInfo Root => GetRoot(new DirectoryInfo(AppContext.BaseDirectory));

        public static DirectoryInfo GetRoot(DirectoryInfo baseAddress)
        {
            var configured = Config.Get("Blob:RootPath", "App_Data/Files");
            var path = Path.IsPathRooted(configured) ? configured : Path.Combine(baseAddress.FullName, configured);
            return new DirectoryInfo(path);
        }

        FileInfo File(Blob blob) => new FileInfo(Path.Combine(Root.FullName, blob.FolderName ?? "", blob.OwnerId() + Path.GetExtension(blob.FileName ?? "")));

        public virtual async Task SaveAsync(Blob blob)
        {
            var file = File(blob);
            file.Directory.Create();
            using (var stream = file.Create()) await stream.WriteAsync(blob.NewFileData, 0, blob.NewFileData.Length);
        }

        public virtual Task DeleteAsync(Blob blob)
        {
            var file = File(blob);
            if (file.Exists) file.Delete();
            return Task.CompletedTask;
        }

        public virtual async Task<byte[]> LoadAsync(Blob blob)
        {
            var file = File(blob);
            if (!file.Exists) return new byte[0];
            using (var stream = file.OpenRead())
            {
                var data = new byte[stream.Length];
                var read = 0;
                while (read < data.Length) read += await stream.ReadAsync(data, read, data.Length - read);
                return data;
            }
        }

        public virtual Task<bool> FileExistsAsync(Blob blob) => Task.FromResult(File(blob).Exists);

        public virtual bool CostsToCheckExistence() => false;
    }

    public class Blob : IComparable<Blob>, IComparable
    {
        public static string EMPTY_FILE = "NoFile.Empty";
        public static string UNCHANGED_FILE = "«UNCHANGED»";
        public static string DefaultEncryptionKey = "Default_ENC_Key:_This_Better_Be_Calculated_If_Possible";

        static readonly string[] UnsafeExtensions = { "aspx", "ascx", "ashx", "axd", "master", "bat", "bas", "asp", "app", "bin", "cla", "class", "cmd", "com", "sitemap", "skin",
            "cshtml", "exe", "fon", "hta", "inf", "ins", "isp", "jse", "js", "jar", "lnk", "msc", "msi", "msp", "mst", "ocx", "pif", "reg", "scr", "sct", "shs", "shb", "sys", "vb", "vbe", "vbs", "vxd", "wsc", "wsf", "wsh", "dll" };

        public byte[] CachedFileData;
        public byte[] NewFileData;
        Entity owner;
        bool isEmpty;

        public Blob() { }

        public Blob(string fileName) => FileName = fileName;

        public Blob(byte[] data, string fileName)
        {
            NewFileData = data;
            FileName = fileName;
        }

        public Blob(FileInfo file) : this(System.IO.File.ReadAllBytes(file.FullName), file.Name) { }

        public string OwnerProperty { get; private set; }

        public string FileName { get; set; }

        public string FolderName { get; set; }

        public string FileExtension => Path.GetExtension(FileName ?? "");

        public string FileNameWithoutExtension => Path.GetFileNameWithoutExtension(FileName ?? "");

        public static Blob Empty() => new Blob(EMPTY_FILE) { isEmpty = true };

        public static Blob Unchanged() => new Blob(UNCHANGED_FILE);

        public static bool HasFileDataInMemory(Blob blob) => blob.NewFileData != null && blob.NewFileData.Length > 0;

        public virtual async Task<byte[]> GetFileDataAsync()
        {
            if (IsEmpty()) return new byte[0];
            if (NewFileData != null && NewFileData.Length > 0) return NewFileData;
            return CachedFileData ?? (CachedFileData = await BlobStorageProviderFactory.Get(this).LoadAsync(this));
        }

        public void SetData(byte[] data)
        {
            NewFileData = data;
            isEmpty = false;
        }

        public async Task<string> GetContentTextAsync() => System.Text.Encoding.UTF8.GetString(await GetFileDataAsync());

        public string Url() => IsEmpty() ? null : $"/file/{FolderName}/{OwnerId()}{FileExtension}";

        public string UrlOr(string defaultUrl) => IsEmpty() ? defaultUrl : Url();

        public string GetCacheSafeUrl() => Url() is { } url ? url + "?" + (FileName?.GetHashCode() ?? 0) : null;

        public bool IsUnchanged() => FileName == UNCHANGED_FILE;

        public bool IsEmpty() => isEmpty || string.IsNullOrEmpty(FileName) || FileName == EMPTY_FILE;

        public bool HasValue() => !IsEmpty();

        public Task<Blob> CloneAsync() => CloneAsync(attach: false, @readonly: false);

        public Blob Clone() => CloneAsync().GetAwaiter().GetResult();

        public async Task<Blob> CloneAsync(bool attach, bool @readonly)
        {
            var clone = new Blob(@readonly ? NewFileData : await GetFileDataAsync(), FileName) { FolderName = FolderName, isEmpty = isEmpty };
            if (attach && owner != null) clone.Attach(owner, OwnerProperty);
            return clone;
        }

        public Blob Attach(Entity owner, string propertyName)
        {
            this.owner = owner;
            OwnerProperty = propertyName;
            FolderName = owner.GetType().Name + "." + propertyName;
            return this;
        }

        public void Detach()
        {
            owner = null;
            OwnerProperty = null;
        }

        public virtual async Task Save()
        {
            if (IsUnchanged()) return;
            var provider = BlobStorageProviderFactory.Get(this);
            if (IsEmpty()) await provider.DeleteAsync(this);
            else if (HasFileDataInMemory(this)) await provider.SaveAsync(this);
        }

        public string GetMimeType() => MimeTypes.TryGetValue(FileExtension.TrimStart('.').ToLowerInvariant(), out var mime) ? mime : "application/octet-stream";

        public bool IsMedia() => GetMimeType().StartsWith("image/") || GetMimeType().StartsWith("audio/") || GetMimeType().StartsWith("video/");

        public static async Task<Blob> FromReference(string reference)
        {
            var parts = reference.Split('/');
            if (parts.Length < 3) throw new ArgumentException("Invalid blob reference: " + reference);
            var type = EntityFinder.GetEntityType(parts[0]) ?? throw new ArgumentException("Unknown entity type: " + parts[0]);
            var entity = await Context.Current.Database().Get(parts[1], type);
            return (Blob)type.GetProperty(parts[2]).GetValue(entity);
        }

        public string OwnerId() => owner?.GetId()?.ToString();

        public static string[] GetUnsafeExtensions() => UnsafeExtensions;

        public bool HasUnsafeExtension() => HasUnsafeFileExtension(FileName);

        public static bool HasUnsafeFileExtension(string fileName) =>
            !string.IsNullOrEmpty(fileName) && UnsafeExtensions.Contains(Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant());

        public override string ToString() => FileName;

        public override bool Equals(object obj) => obj is Blob other && other.FileName == FileName && other.OwnerId() == OwnerId() && other.OwnerProperty == OwnerProperty;

        public override int GetHashCode() => (FileName ?? "").GetHashCode();

        public static bool operator ==(Blob left, Blob right) => ReferenceEquals(left, right) || (!ReferenceEquals(left, null) && left.Equals(right));

        public static bool operator !=(Blob left, Blob right) => !(left == right);

        public Blob Or(Blob other) => IsEmpty() ? other : this;

        public int CompareTo(Blob other) => other == null ? 1 : string.Compare(FileName, other.FileName, StringComparison.Ordinal);

        public int CompareTo(object obj) => CompareTo(obj as Blob);

        static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string>
        {
            ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["png"] = "image/png", ["gif"] = "image/gif", ["bmp"] = "image/bmp", ["svg"] = "image/svg+xml", ["webp"] = "image/webp",
            ["pdf"] = "application/pdf", ["doc"] = "application/msword", ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ["xls"] = "application/vnd.ms-excel", ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ["csv"] = "text/csv", ["txt"] = "text/plain",
            ["html"] = "text/html", ["htm"] = "text/html", ["xml"] = "text/xml", ["json"] = "application/json", ["zip"] = "application/zip",
            ["mp3"] = "audio/mpeg", ["wav"] = "audio/wav", ["mp4"] = "video/mp4", ["mov"] = "video/quicktime",
        };
    }

    public static class BlobExtension
    {
        public static bool IsImage(this Blob doc) => doc.GetMimeType().StartsWith("image/");

        public static string GetKey(this Blob document) => (document.FolderName + "/" + document.OwnerId() + document.FileExtension).TrimStart('/');
    }
}
