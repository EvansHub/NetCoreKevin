﻿using Kevin.Common.TieredServiceRegistration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories
{
    internal class ModuleInitializer : IModuleInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            Console.WriteLine("Repositories-ModuleInitializer");
        }
    }
}
