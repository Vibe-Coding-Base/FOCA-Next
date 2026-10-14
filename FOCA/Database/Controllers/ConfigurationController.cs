using FOCA.Utilites;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace FOCA.Database.Controllers
{
    public class ConfigurationController : BaseController<Configuration>
    {
        public void Save(Configuration item)
        {
            try
            {
                using (FocaContextDb context = new FocaContextDb())
                {
                    var config = context.Configurations.FirstOrDefault();
                    if (config == null)
                    {
                        config = new Configuration();
                        context.Configurations.Add(config);
                    }

                    config.AvaliableTechExtensions = item.AvaliableTechExtensions;
                    config.BingApiKey = SecretProtector.Protect(item.BingApiKey);
                    config.DefaultDnsCacheSnooping = item.DefaultDnsCacheSnooping;
                    config.FingerPrintingAllFtp = item.FingerPrintingAllFtp;
                    config.FingerPrintingAllHttp = item.FingerPrintingAllHttp;
                    config.FingerPrintingAllSmtp = item.FingerPrintingAllSmtp;
                    config.FingerPrintingDns = item.FingerPrintingDns;
                    config.GoogleApiCx = item.GoogleApiCx;
                    config.GoogleApiKey = SecretProtector.Protect(item.GoogleApiKey);
                    config.MaxRecursion = item.MaxRecursion;
                    config.SimultaneousDownloads = item.SimultaneousDownloads;
                    config.ProjectConfigFile = item.ProjectConfigFile;
                    config.ParallelDnsQueries = item.ParallelDnsQueries;
                    config.UseAllDns = item.UseAllDns;
                    config.UseHead = item.UseHead;
                    config.SelectedTechExtensions = item.SelectedTechExtensions;
                    config.SPathsPlugins = item.SPathsPlugins;
                    config.ShodanApiKey = SecretProtector.Protect(item.ShodanApiKey);
                    config.DiarioAPIKey = SecretProtector.Protect(item.DiarioAPIKey);
                    config.DiarioAPISecret = SecretProtector.Protect(item.DiarioAPISecret);

                    context.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public Configuration GetConfiguration()
        {
            using (FocaContextDb context = new FocaContextDb())
            {
                Configuration config = context.Configurations.AsNoTracking().FirstOrDefault() ?? LoadDefaultConfig();

                config.BingApiKey = SecretProtector.Unprotect(config.BingApiKey);
                config.GoogleApiKey = SecretProtector.Unprotect(config.GoogleApiKey);
                config.ShodanApiKey = SecretProtector.Unprotect(config.ShodanApiKey);
                config.DiarioAPIKey = SecretProtector.Unprotect(config.DiarioAPIKey);
                config.DiarioAPISecret = SecretProtector.Unprotect(config.DiarioAPISecret);
                return config;
            }
        }

        private Configuration LoadDefaultConfig()
        {
            var config = new Configuration
            {
                FingerPrintingAllFtp = false,
                FingerPrintingAllHttp = true,
                FingerPrintingAllSmtp = true,
                FingerPrintingDns = true,
                PassiveFingerPrintingHttp = true,
                PasiveFingerPrintingSmtp = true,
                ResolveHost = true,
                UseHead = true,
                DefaultDnsCacheSnooping = "www.google.com",
                GoogleApiKey = string.Empty,
                GoogleApiCx = string.Empty,
                BingApiKey = string.Empty,
                ShodanApiKey = string.Empty,
                NumberOfTasks = 15,
                MaxRecursion = 4,
                ParallelDnsQueries = 4,
                ProjectConfigFile = string.Empty,
                ScanNetranges255 = true,
                SelectedTechExtensions = new List<string>(),
                SimultaneousDownloads = 15,
                SPathsPlugins = string.Empty,
                UseAllDns = true,
                webSearcherEngine = 2,
                DiarioAPIKey = "LfUxRse6mjsRA3DZNBuq",
                DiarioAPISecret = "ThyDYxcaGdHHK6TC3YcMqG2kFexF4D8djmdpcMy8"
            };

            using (FocaContextDb context = new FocaContextDb())
            {
                context.Configurations.Add(config);
                context.SaveChanges();
            }

            return config;
        }
    }
}
