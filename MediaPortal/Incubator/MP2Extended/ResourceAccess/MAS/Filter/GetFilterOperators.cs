﻿using System.Linq;
using HttpServer;
using MediaPortal.Common;
using MediaPortal.Common.Logging;
using MediaPortal.Plugins.MP2Extended.Common;
using MediaPortal.Plugins.MP2Extended.Filters;

namespace MediaPortal.Plugins.MP2Extended.ResourceAccess.MAS.Filter
{
  internal class GetFilterOperators : IRequestMicroModuleHandler
  {
    public dynamic Process(IHttpRequest request)
    {
      return Operator.GetAll().Select(x => new WebFilterOperator()
      {
        Operator = x.Syntax,
        SuitableTypes = x.Types.ToList(),
        Title = x.Name
      }).ToList();
    }

    internal static ILogger Logger
    {
      get { return ServiceRegistration.Get<ILogger>(); }
    }
  }
}