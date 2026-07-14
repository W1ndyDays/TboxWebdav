using Microsoft.Extensions.Logging;
using System.Xml.Linq;
using TboxWebdav.Server.Modules.Tbox.Services;
using TboxWebdav.Server.Modules.Webdav.Internal.Helpers;
using TboxWebdav.Server.Modules.Webdav.Internal.Locking;
using TboxWebdav.Server.Modules.Webdav.Internal.Props;
using TboxWebdav.Server.Modules.Webdav.Internal;
using TboxWebdav.Server.Modules.Webdav.Internal.Stores;
using TboxWebdav.Server.Modules.Tbox.Models;
using Microsoft.AspNetCore.Http;
using TboxWebdav.Server.Modules.Webdav;
using TboxWebdav.Server.Modules.Tbox.Models.Convertion;
using Microsoft.Extensions.DependencyInjection;

namespace TboxWebdav.Server.Modules.Tbox
{
    public class TboxStoreCollection : IStoreCollection
    {
        private readonly ILogger<TboxStoreCollection> _logger;
        private static readonly XElement s_xDavCollection = new XElement(WebDavNamespaces.DavNs + "collection");
        public TboxFolderInfoDto _folderInfo;
        private readonly IWebDavStoreContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly TboxService _tbox;
        private readonly TboxSpaceCredProvider _credProvider;
        private readonly TboxSpaceInfoProvider _spaceInfoProvider;
        private TboxSpaceQuotaInfo quotaInfo;

        public TboxStoreCollection(ILogger<TboxStoreCollection> logger, IWebDavStoreContext context, TboxService tbox, TboxSpaceCredProvider credProvider, TboxSpaceInfoProvider spaceInfoProvider, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _context = context;
            _credProvider = credProvider;
            _spaceInfoProvider = spaceInfoProvider;
            _tbox = tbox;
            _serviceProvider = serviceProvider;
        }

        public void SetFolderInfo(TboxFolderInfoDto folderInfo)
        {
            _folderInfo = folderInfo;
            quotaInfo = _spaceInfoProvider.GetSpaceInfo();
        }

        public PropertyManager<TboxStoreCollection> DefaultPropertyManager { get; } = new PropertyManager<TboxStoreCollection>(new DavProperty<TboxStoreCollection>[]
        {
            // RFC-2518 properties
            new DavCreationDate<TboxStoreCollection>
            {
                Getter = (context, collection) => collection._folderInfo.CreationTime,
                Setter = (context, collection, value) =>
                {
                    collection._folderInfo.CreationTime = value;
                    return DavStatusCode.Ok;
                }
            },
            new DavDisplayName<TboxStoreCollection>
            {
                Getter = (context, collection) => collection._folderInfo.Name
            },
            new DavGetLastModified<TboxStoreCollection>
            {
                Getter = (context, collection) => collection._folderInfo.ModificationTime,
                Setter = (context, collection, value) =>
                {
                    collection._folderInfo.ModificationTime = value;
                    return DavStatusCode.Ok;
                }
            },
            new DavGetContentLength<TboxStoreCollection>
            {
                Getter = (context, collection) => 0L
            },
            new DavGetResourceType<TboxStoreCollection>
            {
                Getter = (context, collection) => new []{s_xDavCollection}
            },

            // Default locking property handling via the LockingManager
            //new DavLockDiscoveryDefault<TboxStoreCollection>(),
            new DavSupportedLockDefault<TboxStoreCollection>(),

            // Hopmann/Lippert collection properties
            //new DavExtCollectionChildCount<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.ItemCount
            //},
            //new DavExtCollectionIsFolder<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => true
            //},
            //new DavExtCollectionIsHidden<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => (collection._folderInfo.IsDisplay)
            //},
            //new DavExtCollectionIsStructuredDocument<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => false
            //},
            //new DavExtCollectionHasSubs<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => (collection._folderInfo.DirectoryCount > 0)
            //},
            //new DavExtCollectionNoSubs<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => false
            //},
            //new DavExtCollectionObjectCount<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.FileCount
            //},
            //new DavExtCollectionReserved<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => !collection.IsWritable
            //},
            //new DavExtCollectionVisibleCount<TboxStoreCollection>
            //{
            //    Getter = (context, collection) =>
            //        collection._folderInfo.VisibleCount
            //},

            // Win32 extensions
            //new Win32CreationTime<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.CreationTimeUtc,
            //    Setter = (context, collection, value) =>
            //    {
            //        collection._folderInfo.CreationTimeUtc = value;
            //        return DavStatusCode.Ok;
            //    }
            //},
            //new Win32LastAccessTime<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.LastAccessTimeUtc,
            //    Setter = (context, collection, value) =>
            //    {
            //        collection._folderInfo.LastAccessTimeUtc = value;
            //        return DavStatusCode.Ok;
            //    }
            //},
            //new Win32LastModifiedTime<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.LastWriteTimeUtc,
            //    Setter = (context, collection, value) =>
            //    {
            //        collection._folderInfo.LastWriteTimeUtc = value;
            //        return DavStatusCode.Ok;
            //    }
            //},
            //new Win32FileAttributes<TboxStoreCollection>
            //{
            //    Getter = (context, collection) => collection._folderInfo.Attributes,
            //    Setter = (context, collection, value) =>
            //    {
            //        collection._folderInfo.Attributes = value;
            //        return DavStatusCode.Ok;
            //    }
            //}
            new DavQuotaAvailableBytes<TboxStoreCollection>
            {
                Getter = (context, collection) => collection.quotaInfo.AvailableSpace != null ? long.Parse(collection.quotaInfo.AvailableSpace) : 0,
                Setter = (context, collection, value) =>
                {
                    return DavStatusCode.Conflict;
                }
            },
            new DavQuotaUsedBytes<TboxStoreCollection>
            {
                Getter = (context, collection) => collection.quotaInfo.Size != null ? long.Parse(collection.quotaInfo.Size) : 0,
                Setter = (context, collection, value) =>
                {
                    return DavStatusCode.Conflict;
                }
            }
        });

        public bool IsWritable => _context.IsWritable;
        public string Name => _folderInfo.Name;
        public string MimeType => throw new Exception();
        public string UniqueKey => string.Join('/', _folderInfo.Path);
        public string FullPath => string.Join('/', _folderInfo.Path);

        // Tbox collections (a.k.a. directories don't have their own data)
        public Task<Stream> GetReadableStreamAsync(HttpContext httpContext) => Task.FromResult((Stream)null);

        public Task<Stream> GetReadableStreamAsync(HttpContext httpContext, long? start, long? end) => Task.FromResult((Stream)null);

        public IPropertyManager PropertyManager => DefaultPropertyManager;
        public ILockingManager LockingManager => _context.LockingManager;

        public Task<IStoreItem> GetItemAsync(string name, HttpContext httpContext)
        {
            // Determine the full path
            var fullPath = UriHelper.Combine(FullPath, name);

            var res = _tbox.GetItemInfo(fullPath);

            if (!res.Success)
            {
                // The item doesn't exist
                return Task.FromResult<IStoreItem>(null);
            }

            if (res.Result.Type == "dir")
            {
                // Check if it's a directory
                var item = _serviceProvider.GetService<IStoreCollection>();
                (item as TboxStoreCollection).SetFolderInfo(res.Result.ToTboxFolderInfoDto());
                return Task.FromResult<IStoreItem>(item);
            }
            else
            {
                // Check if it's a file
                var item = _serviceProvider.GetService<IStoreItem>();
                (item as TboxStoreItem).SetFileInfo(res.Result.ToTboxFileInfoDto());
                return Task.FromResult<IStoreItem>(item);
            }
        }

        public Task<IEnumerable<IStoreItem>> GetItemsAsync(HttpContext httpContext)
        {
            IEnumerable<IStoreItem> GetItemsInternal(List<TboxMergedItemDto> list)
            {
                // Add all directories
                // Add all files
                foreach (var subItem in list)
                {
                    if (subItem.Type == "dir")
                    {
                        var item = _serviceProvider.GetService<IStoreCollection>();
                        (item as TboxStoreCollection).SetFolderInfo(subItem.ToTboxFolderInfoDto());
                        yield return item;
                    }
                    else
                    {
                        var item = _serviceProvider.GetService<IStoreItem>();
                        (item as TboxStoreItem).SetFileInfo(subItem.ToTboxFileInfoDto());
                        yield return item;
                    }
                }

                //if (Config.SharedEnabled && FullPath == "/")
                //    yield return JboxSpecialCollection_Shared.getInstance(LockingManager, JboxSpecialCollectionType.Shared);

                //if (Config.PublicEnabled && FullPath == "/")
                //    yield return JboxSpecialCollection_Public.getInstance(LockingManager);
            }

            var itemList = new List<TboxMergedItemDto>();
            int page = 1;
            int pageSize = 50;
            var res = _tbox.ListItems(FullPath, page, pageSize);
            if (!res.Success)
            {
                _logger.LogError($"list items error: {res.Message}");
                throw new Exception();
            }
            itemList.AddRange(res.Result.Contents);
            for (int i = 1; i <= (res.Result.TotalNum - 1) / pageSize; i++)
            {
                res = _tbox.ListItems(FullPath, i + 1, pageSize);
                if (!res.Success)
                    break;
                itemList.AddRange(res.Result.Contents);
            }
            if (!res.Success)
            {
                _logger.LogError($"list items error: {res.Message}");
                throw new Exception();
            }

            return Task.FromResult(GetItemsInternal(itemList));
        }

        public Task<StoreItemResult> CreateItemAsync(string name, bool overwrite, HttpContext httpContext)
        {
            //// Return error
            //if (!IsWritable)
            //    return Task.FromResult(new StoreItemResult(DavStatusCode.PreconditionFailed));

            //// Determine the destination path
            //var destinationPath = Path.Combine(FullPath, name);

            //// Determine result
            //DavStatusCode result;

            //// Check if the file can be overwritten
            //if (File.Exists(name))
            //{
            //    if (!overwrite)
            //        return Task.FromResult(new StoreItemResult(DavStatusCode.PreconditionFailed));

            //    result = DavStatusCode.NoContent;
            //}
            //else
            //{
            //    result = DavStatusCode.Created;
            //}

            //try
            //{
            //    // Create a new file
            //    File.Create(destinationPath).Dispose();
            //}
            //catch (Exception exc)
            //{
            //    // Log exception
            //    s_log.Log(LogLevel.Error, () => $"Unable to create '{destinationPath}' file.", exc);
            //    return Task.FromResult(new StoreItemResult(DavStatusCode.InternalServerError));
            //}

            //// Return result
            //return Task.FromResult(new StoreItemResult(result, new JboxStoreItem(LockingManager, new JboxFileInfo(destinationPath), IsWritable)));
            throw new NotImplementedException("Not Supported");
        }

        public async Task<DavStatusCode> UploadFromStreamAsync(HttpContext httpContext, string name, Stream inputStream, long length)
        {
            // Check if the item is writable
            if (!IsWritable)
                return DavStatusCode.Forbidden;

            // Copy the stream
            try
            {
                var uploader = _serviceProvider.GetService<TboxUploader>();
                uploader.Init(UriHelper.Combine(FullPath, name), inputStream, length);
                var res = uploader.Run();
                if (!res.success)
                {
                    _logger.LogError($"upload failed: {res.result}");
                    return DavStatusCode.InternalServerError;
                }
                return DavStatusCode.Ok;
            }
            //catch (IOException ioException) when (ioException.IsJboxFull())
            //{
            //    return DavStatusCode.InsufficientStorage;
            //}
            catch (Exception ex)
            {
                return DavStatusCode.InternalServerError;
            }
        }

        public async Task<DavStatusCode> CreateCollectionAsync(string name, bool overwrite, HttpContext httpContext)
        {
            // Return error
            if (!IsWritable)
                return DavStatusCode.PreconditionFailed;

            // Determine the destination path
            var destinationPath = UriHelper.Combine(FullPath, name);

            // Check if the directory can be overwritten
            DavStatusCode result;
            try
            {
                var res = _tbox.CreateDirectory(destinationPath);
                if (res.Success)
                    result = DavStatusCode.Created;
                else if (res.Result.Code == "SameNameDirectoryOrFileExists")
                    result = DavStatusCode.Created;
                else
                    result = DavStatusCode.NoContent;
            }
            catch (Exception exc)
            {
                // Log exception
                _logger.Log(LogLevel.Error, $"Unable to create '{destinationPath}' directory.", exc);
                return DavStatusCode.InternalServerError;
            }

            // Return the collection
            return result;
            //throw new NotImplementedException("Not Supported");
        }

        public async Task<DavStatusCode> CopyAsync(IStoreCollection destinationCollection, string name, bool overwrite, HttpContext httpContext)
        {
            //Todo
            // Just create the folder itself
            var result = await destinationCollection.CreateCollectionAsync(name, overwrite, httpContext).ConfigureAwait(false);
            return result;
        }

        public bool SupportsFastMove(IStoreCollection destination, string destinationName, bool overwrite, HttpContext httpContext)
        {
            return true;
        }

        public async Task<StoreItemResult> MoveItemAsync(string sourceName, IStoreCollection destinationCollection, string destinationName, bool overwrite, HttpContext httpContext)
        {
            //Use direct move now
            throw new NotImplementedException("Not Supported");
            //// Return error
            //if (!IsWritable)
            //    return new StoreItemResult(DavStatusCode.PreconditionFailed);

            //// Determine the object that is being moved
            //var item = await GetItemAsync(sourceName, httpContext).ConfigureAwait(false);
            //if (item == null)
            //    return new StoreItemResult(DavStatusCode.NotFound);

            //try
            //{
            //    if (destinationCollection is not TboxStoreCollection destinationTboxStoreCollection)
            //    {
            //        //// Attempt to copy the item to the destination collection
            //        //var result = await item.CopyAsync(destinationCollection, destinationName, overwrite, httpContext).ConfigureAwait(false);
            //        //if (result.Result == DavStatusCode.Created || result.Result == DavStatusCode.NoContent)
            //        //    await DeleteItemAsync(sourceName, httpContext).ConfigureAwait(false);

            //        //// Return the result
            //        //return result;
            //        throw new Exception("the destination collection is not a directory");
            //    }
            //    else// If the destination collection is a directory too, then we can simply move the file
            //    {
            //        // Return error
            //        if (!destinationTboxStoreCollection.IsWritable)
            //            return new StoreItemResult(DavStatusCode.PreconditionFailed);

            //        // Determine source and destination paths
            //        var sourcePath = UriHelper.Combine(_folderInfo.Path, sourceName);
            //        var destinationPath = UriHelper.Combine(destinationTboxStoreCollection._folderInfo.Path, destinationName);

            //        // Check if the file already exists
            //        DavStatusCode result;

            //        var res = JboxService.GetJboxItemInfo(destinationPath);

            //        if (res.success)
            //        {
            //            if (!overwrite)
            //                return new StoreItemResult(DavStatusCode.PreconditionFailed);

            //            JboxService.DeleteJboxItem(res);
            //            result = DavStatusCode.NoContent;
            //        }
            //        else
            //            result = DavStatusCode.Created;

            //        switch (item)
            //        {
            //            case JboxStoreItem _:
            //                // Move the file
            //                if (destinationTboxStoreCollection._folderInfo.Path == _folderInfo.Path)
            //                {
            //                    JboxService.RenameJboxItem(sourcePath, destinationPath);
            //                }
            //                else
            //                {
            //                    JboxService.MoveJboxItem(sourcePath, destinationTboxStoreCollection._folderInfo.Path);
            //                }

            //                var item1 = JboxService.GetJboxItemInfo(destinationPath);
            //                if (!item1.success)
            //                    return new StoreItemResult(DavStatusCode.Conflict);

            //                return new StoreItemResult(result, new JboxStoreItem(LockingManager, item1.ToJboxFileInfo(), IsWritable));

            //            case TboxStoreCollection _:
            //                // Move the directory
            //                if (destinationTboxStoreCollection._folderInfo.Path == _folderInfo.Path)
            //                {
            //                    JboxService.RenameJboxItem(sourcePath, destinationPath);
            //                }
            //                else
            //                {
            //                    JboxService.MoveJboxItem(sourcePath, destinationTboxStoreCollection._folderInfo.Path);
            //                }

            //                var item2 = JboxService.GetJboxItemInfo(destinationPath);
            //                if (!item2.success)
            //                    return new StoreItemResult(DavStatusCode.Conflict);

            //                return new StoreItemResult(result, new TboxStoreCollection(LockingManager, item2.ToJboxDirectoryInfo(), IsWritable));
            //            default:
            //                return new StoreItemResult(DavStatusCode.InternalServerError);
            //        }
            //    }
            //}
            //catch (UnauthorizedAccessException)
            //{
            //    return new StoreItemResult(DavStatusCode.Forbidden);
            //}
        }

        public Task<DavStatusCode> DeleteItemAsync(string name, HttpContext httpContext)
        {
            //Use direct delete now
            throw new NotImplementedException("Not Supported");
            //// Return error
            //if (!IsWritable)
            //    return Task.FromResult(DavStatusCode.PreconditionFailed);

            //// Determine the full path
            //var fullPath = UriHelper.Combine(_folderInfo.Path, name);
            //try
            //{
            //    // Check if the item exists
            //    var item = JboxService.GetJboxItemInfo(fullPath);
            //    if (item.success)
            //    {
            //        JboxService.DeleteJboxItem(fullPath);
            //        return Task.FromResult(DavStatusCode.Ok);
            //    }
            //    // Item not found
            //    return Task.FromResult(DavStatusCode.NotFound);
            //}
            //catch (UnauthorizedAccessException)
            //{
            //    return Task.FromResult(DavStatusCode.Forbidden);
            //}
            //catch (Exception exc)
            //{
            //    // Log exception
            //    s_log.Log(LogLevel.Error, () => $"Unable to delete '{fullPath}' directory.", exc);
            //    return Task.FromResult(DavStatusCode.BadRequest);
            //}
        }

        public InfiniteDepthMode InfiniteDepthMode => InfiniteDepthMode.Assume1;

        public override int GetHashCode()
        {
            return _folderInfo.Path.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var storeCollection = obj as TboxStoreCollection;
            if (storeCollection == null)
                return false;
            return storeCollection.FullPath.Equals(FullPath, StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
