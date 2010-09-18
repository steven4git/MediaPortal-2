#region Copyright (C) 2007-2010 Team MediaPortal

/*
    Copyright (C) 2007-2010 Team MediaPortal
    http://www.team-mediaportal.com

    This file is part of MediaPortal 2

    MediaPortal 2 is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    MediaPortal 2 is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with MediaPortal 2.  If not, see <http://www.gnu.org/licenses/>.
*/

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using MediaPortal.Core.General;
using MediaPortal.Core.MediaManagement;

namespace MediaPortal.Core.Services.MediaManagement
{
  public class RemoteFileSystemResourceAccessor : RemoteResourceAccessorBase, IFileSystemResourceAccessor
  {
    protected long? _sizeCache = null;
    protected DateTime? _lastChangedCache = null;

    protected RemoteFileSystemResourceAccessor(SystemName nativeSystem, ResourcePath nativeResourcePath, bool isFile,
        string resourcePathName, string resourceName, long length, DateTime lastChanged) :
        this(nativeSystem, nativeResourcePath, isFile, resourcePathName, resourceName)
    {
      _lastChangedCache = lastChanged;
      _sizeCache = length;
    }

    protected RemoteFileSystemResourceAccessor(SystemName nativeSystem, ResourcePath nativeResourcePath, bool isFile,
        string resourcePathName, string resourceName) :
        base(nativeSystem, nativeResourcePath, isFile, resourcePathName, resourceName) { }

    public static bool ConnectFileSystem(SystemName nativeSystem, ResourcePath nativeResourcePath,
        out IFileSystemResourceAccessor result)
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      result = null;
      bool isFileSystemResource;
      bool isFile;
      string resourcePathName;
      string resourceName;
      long size;
      DateTime lastChanged;
      if (!rris.GetResourceInformation(nativeSystem, nativeResourcePath, out isFileSystemResource, out isFile,
          out resourcePathName, out resourceName, out lastChanged, out size) || !isFileSystemResource)
        return false;
      result = new RemoteFileSystemResourceAccessor(nativeSystem, nativeResourcePath, isFile,
          resourcePathName, resourceName, size, lastChanged);
      return true;
    }

    protected ICollection<IFileSystemResourceAccessor> WrapResourcePathsData(ICollection<ResourcePathMetadata> resourcesData)
    {
      ICollection<IFileSystemResourceAccessor> result = new List<IFileSystemResourceAccessor>();
      foreach (ResourcePathMetadata fileData in resourcesData)
        result.Add(new RemoteFileSystemResourceAccessor(_nativeSystem, fileData.ResourcePath,
            true, fileData.HumanReadablePath, fileData.ResourceName));
      return result;
    }

    protected void FillCaches()
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      bool isFileSystemResource;
      bool isFile;
      string resourcePathName;
      string resourceName;
      DateTime lastChanged;
      long size;
      if (!rris.GetResourceInformation(_nativeSystem, _nativeResourcePath, out isFileSystemResource, out isFile,
          out resourcePathName, out resourceName, out lastChanged, out size))
        throw new IOException(string.Format("Unable to get file information for file '{0}' on system '{1}'", _nativeResourcePath, _nativeSystem));
      _lastChangedCache = lastChanged;
      _sizeCache = size;
    }

    public override long Size
    {
      get
      {
        if (_sizeCache.HasValue)
          return _sizeCache.Value;
        FillCaches();
        return _sizeCache.Value;
      }
    }

    #region IFileSystemResourceAccessor implementation

    public bool IsDirectory
    {
      get { return !_isFile; }
    }

    public bool Exists(string path)
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      ResourcePath resourcePath = rris.ConcatenatePaths(_nativeSystem, _nativeResourcePath, path);
      return rris.ResourceExists(_nativeSystem, resourcePath);
    }

    public IResourceAccessor GetResource(string path)
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      ResourcePath resourcePath = rris.ConcatenatePaths(_nativeSystem, _nativeResourcePath, path);
      IFileSystemResourceAccessor result;
      return ConnectFileSystem(_nativeSystem, resourcePath, out result) ? result : null;
    }

    public override DateTime LastChanged
    {
      get
      {
        if (_lastChangedCache.HasValue)
          return _lastChangedCache.Value;
        FillCaches();
        return _lastChangedCache.Value;
      }
    }

    public ICollection<IFileSystemResourceAccessor> GetFiles()
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      ICollection<ResourcePathMetadata> filesData = rris.GetFiles(_nativeSystem, _nativeResourcePath);
      return WrapResourcePathsData(filesData);
    }

    public ICollection<IFileSystemResourceAccessor> GetChildDirectories()
    {
      IRemoteResourceInformationService rris = ServiceRegistration.Get<IRemoteResourceInformationService>();
      ICollection<ResourcePathMetadata> directoriesData = rris.GetChildDirectories(_nativeSystem, _nativeResourcePath);
      return WrapResourcePathsData(directoriesData);
    }

    #endregion
  }
}