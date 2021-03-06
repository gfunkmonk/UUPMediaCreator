﻿using CompDB;
using Microsoft.Cabinet;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#nullable enable

namespace MediaCreationLib.Planning.NET
{
    public static class FileLocator
    {
        public static HashSet<CompDBXmlClass.CompDB> GetCompDBsFromUUPFiles(string UUPPath)
        {
            HashSet<CompDBXmlClass.CompDB> compDBs = new HashSet<CompDBXmlClass.CompDB>();

            try
            {
                if (Directory.EnumerateFiles(UUPPath, "*aggregatedmetadata*").Count() > 0)
                {
                    using (CabinetHandler cabinet = new CabinetHandler(File.OpenRead(Directory.EnumerateFiles(UUPPath, "*aggregatedmetadata*").First())))
                    {
                        foreach (var file in cabinet.Files.Where(x => x.EndsWith(".xml.cab", StringComparison.InvariantCultureIgnoreCase)))
                        {
                            try
                            {
                                using (CabinetHandler cabinet2 = new CabinetHandler(cabinet.OpenFile(file)))
                                {
                                    string xmlfile = cabinet2.Files.First();
                                    using (Stream xmlstream = cabinet2.OpenFile(xmlfile))
                                    {
                                        compDBs.Add(CompDBXmlClass.DeserializeCompDB(xmlstream));
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
                else
                {
                    IEnumerable<string> files = Directory.EnumerateFiles(UUPPath).Select(x => Path.GetFileName(x)).Where(x => x.EndsWith(".xml.cab", StringComparison.InvariantCultureIgnoreCase));

                    foreach (var file in files)
                    {
                        try
                        {
                            using (CabinetHandler cabinet2 = new CabinetHandler(File.OpenRead(Path.Combine(UUPPath, file))))
                            {
                                string xmlfile = cabinet2.Files.First();
                                using (Stream xmlstream = cabinet2.OpenFile(xmlfile))
                                {
                                    compDBs.Add(CompDBXmlClass.DeserializeCompDB(xmlstream));
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            return compDBs;
        }

        public static (bool, HashSet<string>) VerifyFilesAreAvailableForCompDB(CompDBXmlClass.CompDB compDB, string UUPPath)
        {
            HashSet<string> missingPackages = new HashSet<string>();

            foreach (CompDBXmlClass.Package feature in compDB.Features.Feature[0].Packages.Package)
            {
                CompDBXmlClass.Package pkg = compDB.Packages.Package.First(x => x.ID == feature.ID);

                (bool succeeded, string missingFile) = VerifyFileIsAvailableForPackage(pkg, UUPPath);
                if (!succeeded)
                    missingPackages.Add(missingFile);
            }

            return (missingPackages.Count <= 0, missingPackages);
        }

        public static (bool, string) VerifyFileIsAvailableForPackage(CompDBXmlClass.Package pkg, string UUPPath)
        {
            string missingPackage = "";

            //
            // Some download utilities that start with the letter U and finish with UPDump or start with the letter U and finish with UP.rg-adguard download files without respecting Microsoft filenames
            // We attempt to locate files based on what we think they use first.
            //
            string file = pkg.GetCommonlyUsedIncorrectFileName();

            if (!File.Exists(Path.Combine(UUPPath, file)))
            {
                //
                // Wow, someone actually downloaded UUP files using a tool that respects Microsoft paths, that's exceptional
                //
                file = pkg.Payload.PayloadItem.Path;
                if (!File.Exists(Path.Combine(UUPPath, file)))
                {
                    //
                    // What a disapointment, they simply didn't download everything.. Oops.
                    // TODO: generate missing files out of thin air
                    //
                    missingPackage = file;
                }
            }

            return (string.IsNullOrEmpty(missingPackage), missingPackage);
        }
    }
}
