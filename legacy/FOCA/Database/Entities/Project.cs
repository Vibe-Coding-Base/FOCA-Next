using FOCA.ModifiedComponents;
using FOCA.Utilites;
using Foca.Metadata;
using System;
using System.Collections.Generic;
using System.IO;

namespace FOCA.Database.Entities
{
    [Serializable]
    public class Project : IDisposable
    {
        [NonSerialized]
        public const string DefaultProjectName = "Project Name";

        public const string DomainRequiredMessage =
            "This is a document analysis project without a domain. Add a domain in the project properties to use web search and DNS reconnaissance.";

        public int Id { get; set; }

        public enum ProjectStates { Uninitialized, InitializedUnsave, InitializedSave };

        public string ProjectName { get; set; }

        public string ProjectSaveFile { get; set; }

        public ProjectStates ProjectState { get; set; }

        /// <summary>
        /// Main domain under reconnaissance. Empty for document analysis projects, which work on
        /// local documents and folders; a domain can be set later from the project properties.
        /// </summary>
        public string Domain { get; set; }

        /// <summary>False for document analysis projects. Get-only, so Entity Framework does not map it.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool HasDomain => !String.IsNullOrWhiteSpace(Domain);

        public List<string> AlternativeDomains { get; set; }

        public string FolderToDownload { get; set; }

        public DateTime ProjectDate { get; set; }

        public string ProjectNotes { get; set; }

        public ThreadSafeList<NetRange> LstNetRange = new ThreadSafeList<NetRange>();

        public Project()
        {
            ProjectState = ProjectStates.Uninitialized;
            AlternativeDomains = new List<string>();
            FolderToDownload = Path.GetTempPath();
        }

        public NetRange GetNetrange(string ip)
        {
            return NetRange.GetNetrange(ip);
        }

        public int GetIpsOfNetrange(NetRange netrange)
        {
            var p1 = int.Parse(netrange.to.Split('.')[0]) - (int.Parse(netrange.from.Split('.')[0]));
            var p2 = int.Parse(netrange.to.Split('.')[1]) - (int.Parse(netrange.from.Split('.')[1]));
            var p3 = int.Parse(netrange.to.Split('.')[2]) - (int.Parse(netrange.from.Split('.')[2]));
            var p4 = int.Parse(netrange.to.Split('.')[3]) - (int.Parse(netrange.from.Split('.')[3]));

            if (p1 == 0) p1 = 1;
            if (p2 == 0) p2 = 1;
            if (p3 == 0) p3 = 1;
            if (p4 == 0) p4 = 1;

            return p1 * p2 * p3 * p4;

        }

        /// <summary>
        /// Validate if Ip is in NetRange.
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public bool IsIpInNetrange(string ip)
        {
            for (int i = 0; i < LstNetRange.Count; i++)
            {
                NetRange netrange = LstNetRange[i];
                if (netrange == null)
                    return true;

                if (netrange.IsIpInNetrange(ip))
                    return true;
            }
            return false;
        }

        #region IDisposable Support

        private bool DisposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (DisposedValue) return;

            if (disposing)
            {
                LstNetRange.Clear();
            }

            LstNetRange = null;

            DisposedValue = true;
        }

        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
