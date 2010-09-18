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

using System.Net;
using HttpServer;
using HttpServer.HttpModules;
using MediaPortal.Core.Logging;
using MediaPortal.Core.MediaManagement;
using MediaPortal.Core.Services.MediaManagement.Settings;
using MediaPortal.Core.Settings;

namespace MediaPortal.Core.Services.MediaManagement
{
  public class ResourceServer : IResourceServer
  {
    internal class HttpLogWriter : ILogWriter
    {
      public void Write(object source, LogPrio priority, string message)
      {
        string msg = source + ": " + message;
        ILogger logger = ServiceRegistration.Get<ILogger>();
        switch (priority)
        {
          case LogPrio.Trace:
            // Don't write trace messages (we don't support a trace level in MP - would have to map it to debug level)
            break;
          case LogPrio.Debug:
            logger.Debug(msg);
            break;
          case LogPrio.Info:
            logger.Info(msg);
            break;
          case LogPrio.Warning:
            logger.Warn(msg);
            break;
          case LogPrio.Error:
            logger.Error(msg);
            break;
          case LogPrio.Fatal:
            logger.Critical(msg);
            break;
        }
      }
    }

    protected readonly HttpServer.HttpServer _httpServerV4;
    protected readonly HttpServer.HttpServer _httpServerV6;

    public ResourceServer()
    {
      _httpServerV4 = new HttpServer.HttpServer(new HttpLogWriter());
      _httpServerV6 = new HttpServer.HttpServer(new HttpLogWriter());
      ResourceAccessModule module = new ResourceAccessModule();
      AddHttpModule(module);
    }

    public void StartServers()
    {
      ServerSettings settings = ServiceRegistration.Get<ISettingsManager>().Load<ServerSettings>();
      if (settings.UseIPv4)
      {
        _httpServerV4.Start(IPAddress.Any, settings.HttpServerPort);
        ServiceRegistration.Get<ILogger>().Info("ResourceServer: Started HTTP server (IPv4) at port {0}", _httpServerV4.Port);
      }
      if (settings.UseIPv6)
      {
        _httpServerV6.Start(IPAddress.IPv6Any, settings.HttpServerPort);
        ServiceRegistration.Get<ILogger>().Info("ResourceServer: Started HTTP server (IPv6) at port {0}", _httpServerV6.Port);
      }
    }

    public void StopServers()
    {
      ServiceRegistration.Get<ILogger>().Info("ResourceServer: Shutting down HTTP server");
      _httpServerV4.Stop();
      _httpServerV6.Stop();
    }

    #region IResourceServer implementation

    public int PortIPv4
    {
      get { return _httpServerV4.Port; }
    }

    public int PortIPv6
    {
      get { return _httpServerV6.Port; }
    }

    public void Startup()
    {
      StartServers();
    }

    public void Shutdown()
    {
      StopServers();
    }

    public void RestartHttpServers()
    {
      StopServers();
      StartServers();
    }

    public void AddHttpModule(HttpModule module)
    {
      _httpServerV4.Add(module);
      _httpServerV6.Add(module);
    }

    public void RemoveHttpModule(HttpModule module)
    {
      _httpServerV4.Remove(module);
      _httpServerV6.Remove(module);
    }

    #endregion
  }
}