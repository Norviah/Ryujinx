﻿using JsonPrettyPrinterPlus;
using LibHac;
using LibHac.Fs;
using LibHac.FsSystem;
using LibHac.FsSystem.NcaUtils;
using LibHac.Spl;
using Ryujinx.Common.Logging;
using Ryujinx.HLE.FileSystem;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Utf8Json;
using Utf8Json.Resolvers;
using SystemState = Ryujinx.HLE.HOS.SystemState;

namespace Ryujinx.UI
{
    public class ApplicationLibrary
    {
        public static byte[] RyujinxNspIcon { get; private set; }
        public static byte[] RyujinxXciIcon { get; private set; }
        public static byte[] RyujinxNcaIcon { get; private set; }
        public static byte[] RyujinxNroIcon { get; private set; }
        public static byte[] RyujinxNsoIcon { get; private set; }

        public struct ApplicationData
        {
            public bool   Fav        { get; set; }
            public byte[] Icon       { get; set; }
            public string TitleName  { get; set; }
            public string TitleId    { get; set; }
            public string Developer  { get; set; }
            public string Version    { get; set; }
            public string TimePlayed { get; set; }
            public string LastPlayed { get; set; }
            public string FileExt    { get; set; }
            public string FileSize   { get; set; }
            public string Path       { get; set; }
        }

        public static List<ApplicationData> ApplicationLibraryData { get; private set; }

        private static Keyset KeySet;
        private static SystemState.TitleLanguage DesiredTitleLanguage;

        private const double SecondsPerMinute = 60.0;
        private const double SecondsPerHour   = SecondsPerMinute * 60;
        private const double SecondsPerDay    = SecondsPerHour * 24;

        private struct ApplicationMetadata
        {
            public bool   Fav        { get; set; }
            public double TimePlayed { get; set; }
            public string LastPlayed { get; set; }
        }

        private static ApplicationMetadata AppMetadata;

        public static void Init(List<string> AppDirs, Keyset keySet, SystemState.TitleLanguage desiredTitleLanguage)
        {
            KeySet               = keySet;
            DesiredTitleLanguage = desiredTitleLanguage;

            // Loads the default application Icons
            RyujinxNspIcon = GetResourceBytes("Ryujinx.Ui.assets.ryujinxNSPIcon.png");
            RyujinxXciIcon = GetResourceBytes("Ryujinx.Ui.assets.ryujinxXCIIcon.png");
            RyujinxNcaIcon = GetResourceBytes("Ryujinx.Ui.assets.ryujinxNCAIcon.png");
            RyujinxNroIcon = GetResourceBytes("Ryujinx.Ui.assets.ryujinxNROIcon.png");
            RyujinxNsoIcon = GetResourceBytes("Ryujinx.Ui.assets.ryujinxNSOIcon.png");

            // Builds the applications list with paths to found applications
            List<string> applications = new List<string>();
            foreach (string appDir in AppDirs)
            {
                if (Directory.Exists(appDir) == false)
                {
                    Logger.PrintWarning(LogClass.Application, $"The \"game_dirs\" section in \"Config.json\" contains an invalid directory: \"{appDir}\"");

                    continue;
                }

                string[] apps = Directory.GetFiles(appDir, "*.*", SearchOption.AllDirectories);
                foreach (string app in apps)
                {
                    if ((Path.GetExtension(app) == ".xci") ||
                        (Path.GetExtension(app) == ".nca") ||
                        (Path.GetExtension(app) == ".nsp") ||
                        (Path.GetExtension(app) == ".pfs0")||
                        (Path.GetExtension(app) == ".nro") ||
                        (Path.GetExtension(app) == ".nso"))
                    {
                        applications.Add(app);
                    }
                }
            }

            // Loops through applications list, creating a struct for each application and then adding the struct to a list of structs
            ApplicationLibraryData = new List<ApplicationData>();
            foreach (string applicationPath in applications)
            {
                double filesize        = new FileInfo(applicationPath).Length * 0.000000000931;
                string titleName       = null;
                string titleId         = null;
                string developer       = null;
                string version         = null;
                byte[] applicationIcon = null;

                using (FileStream file = new FileStream(applicationPath, FileMode.Open, FileAccess.Read))
                {
                    if ((Path.GetExtension(applicationPath) == ".nsp")  ||
                        (Path.GetExtension(applicationPath) == ".pfs0") ||
                        (Path.GetExtension(applicationPath) == ".xci"))
                    {
                        try
                        {
                            IFileSystem controlFs = null;

                            // Store the ControlFS in variable called controlFs
                            if (Path.GetExtension(applicationPath) == ".xci")
                            {
                                Xci xci = new Xci(KeySet, file.AsStorage());

                                controlFs = GetControlFs(xci.OpenPartition(XciPartitionType.Secure));
                            }
                            else
                            {
                                controlFs = GetControlFs(new PartitionFileSystem(file.AsStorage()));
                            }

                            // Creates NACP class from the NACP file
                            controlFs.OpenFile(out IFile controlNacpFile, "/control.nacp", OpenMode.Read).ThrowIfFailure();

                            Nacp controlData = new Nacp(controlNacpFile.AsStream());

                            // Get the title name, title ID, developer name and version number from the NACP
                            version = controlData.DisplayVersion;

                            titleName = controlData.Descriptions[(int)DesiredTitleLanguage].Title;

                            if (string.IsNullOrWhiteSpace(titleName))
                            {
                                titleName = controlData.Descriptions.ToList().Find(x => !string.IsNullOrWhiteSpace(x.Title)).Title;
                            }

                            titleId = controlData.PresenceGroupId.ToString("x16");

                            if (string.IsNullOrWhiteSpace(titleId))
                            {
                                titleId = controlData.SaveDataOwnerId.ToString("x16");
                            }

                            if (string.IsNullOrWhiteSpace(titleId))
                            {
                                titleId = (controlData.AddOnContentBaseId - 0x1000).ToString("x16");
                            }

                            developer = controlData.Descriptions[(int)DesiredTitleLanguage].Developer;

                            if (string.IsNullOrWhiteSpace(developer))
                            {
                                developer = controlData.Descriptions.ToList().Find(x => !string.IsNullOrWhiteSpace(x.Developer)).Developer;
                            }

                            // Read the icon from the ControlFS and store it as a byte array
                            try
                            {
                                controlFs.OpenFile(out IFile icon, $"/icon_{DesiredTitleLanguage}.dat", OpenMode.Read).ThrowIfFailure();

                                using (MemoryStream stream = new MemoryStream())
                                {
                                    icon.AsStream().CopyTo(stream);
                                    applicationIcon = stream.ToArray();
                                }
                            }
                            catch (HorizonResultException)
                            {
                                foreach (DirectoryEntryEx entry in controlFs.EnumerateEntries("/", "*"))
                                {
                                    if (entry.Name == "control.nacp")
                                    {
                                        continue;
                                    }

                                    controlFs.OpenFile(out IFile icon, entry.FullPath, OpenMode.Read).ThrowIfFailure();

                                    using (MemoryStream stream = new MemoryStream())
                                    {
                                        icon.AsStream().CopyTo(stream);
                                        applicationIcon = stream.ToArray();
                                    }

                                    if (applicationIcon != null)
                                    {
                                        break;
                                    }
                                }

                                if (applicationIcon == null)
                                {
                                    applicationIcon = NspOrXciIcon(applicationPath);
                                }
                            }
                        }
                        catch (MissingKeyException exception)
                        {
                            titleName       = "Unknown";
                            titleId         = "Unknown";
                            developer       = "Unknown";
                            version         = "?";
                            applicationIcon = NspOrXciIcon(applicationPath);

                            Logger.PrintWarning(LogClass.Application, $"Your key set is missing a key with the name: {exception.Name}");
                        }
                        catch (InvalidDataException)
                        {
                            titleName       = "Unknown";
                            titleId         = "Unknown";
                            developer       = "Unknown";
                            version         = "?";
                            applicationIcon = NspOrXciIcon(applicationPath);

                            Logger.PrintWarning(LogClass.Application, $"The file is not an NCA file or the header key is incorrect. Errored File: {applicationPath}");
                        }
                        catch (Exception exception)
                        {
                            Logger.PrintWarning(LogClass.Application, $"This warning usualy means that you have a DLC in one of you game directories\n{exception}");

                            continue;
                        }
                    }
                    else if (Path.GetExtension(applicationPath) == ".nro")
                    {
                        BinaryReader reader = new BinaryReader(file);

                        byte[] Read(long Position, int Size)
                        {
                            file.Seek(Position, SeekOrigin.Begin);

                            return reader.ReadBytes(Size);
                        }

                        file.Seek(24, SeekOrigin.Begin);
                        int AssetOffset = reader.ReadInt32();

                        if (Encoding.ASCII.GetString(Read(AssetOffset, 4)) == "ASET")
                        {
                            byte[] IconSectionInfo = Read(AssetOffset + 8, 0x10);

                            long iconOffset = BitConverter.ToInt64(IconSectionInfo, 0);
                            long iconSize   = BitConverter.ToInt64(IconSectionInfo, 8);

                            ulong nacpOffset = reader.ReadUInt64();
                            ulong nacpSize   = reader.ReadUInt64();

                            // Reads and stores game icon as byte array
                            applicationIcon = Read(AssetOffset + iconOffset, (int)iconSize);

                            // Creates memory stream out of byte array which is the NACP
                            using (MemoryStream stream = new MemoryStream(Read(AssetOffset + (int)nacpOffset, (int)nacpSize)))
                            {
                                // Creates NACP class from the memory stream
                                Nacp controlData = new Nacp(stream);

                                // Get the title name, title ID, developer name and version number from the NACP
                                version = controlData.DisplayVersion;

                                titleName = controlData.Descriptions[(int)DesiredTitleLanguage].Title;

                                if (string.IsNullOrWhiteSpace(titleName))
                                {
                                    titleName = controlData.Descriptions.ToList().Find(x => !string.IsNullOrWhiteSpace(x.Title)).Title;
                                }

                                titleId = controlData.PresenceGroupId.ToString("x16");

                                if (string.IsNullOrWhiteSpace(titleId))
                                {
                                    titleId = controlData.SaveDataOwnerId.ToString("x16");
                                }

                                if (string.IsNullOrWhiteSpace(titleId))
                                {
                                    titleId = (controlData.AddOnContentBaseId - 0x1000).ToString("x16");
                                }

                                developer = controlData.Descriptions[(int)DesiredTitleLanguage].Developer;

                                if (string.IsNullOrWhiteSpace(developer))
                                {
                                    developer = controlData.Descriptions.ToList().Find(x => !string.IsNullOrWhiteSpace(x.Developer)).Developer;
                                }
                            }
                        }
                        else
                        {
                            applicationIcon = RyujinxNroIcon;
                            titleName       = "Application";
                            titleId         = "0000000000000000";
                            developer       = "Unknown";
                            version         = "?";
                        }
                    }
                    // If its an NCA or NSO we just set defaults
                    else if ((Path.GetExtension(applicationPath) == ".nca") || (Path.GetExtension(applicationPath) == ".nso"))
                    {
                        if (Path.GetExtension(applicationPath) == ".nca")
                        {
                            Nca nca = new Nca(KeySet, new FileStream(applicationPath, FileMode.Open, FileAccess.Read).AsStorage(false));
                            if (nca.Header.ContentType != NcaContentType.Program)
                            {
                                continue;
                            }

                            applicationIcon = RyujinxNcaIcon;
                        }
                        else if (Path.GetExtension(applicationPath) == ".nso")
                        {
                            applicationIcon = RyujinxNsoIcon;
                        }

                        string fileName = Path.GetFileName(applicationPath);
                        string fileExt  = Path.GetExtension(applicationPath);

                        StringBuilder titlename = new StringBuilder();
                        titlename.Append(fileName);
                        titlename.Remove(fileName.Length - fileExt.Length, fileExt.Length);

                        titleName = titlename.ToString();
                        titleId   = "0000000000000000";
                        version   = "?";
                        developer = "Unknown";
                    }
                }

                (bool, string, string) metaData = GetMetadata(titleId);

                ApplicationData data = new ApplicationData()
                {
                    Fav        = metaData.Item1,
                    Icon       = applicationIcon,
                    TitleName  = titleName,
                    TitleId    = titleId,
                    Developer  = developer,
                    Version    = version,
                    TimePlayed = metaData.Item2,
                    LastPlayed = metaData.Item3,
                    FileExt    = Path.GetExtension(applicationPath).ToUpper().Remove(0 ,1),
                    FileSize   = (filesize < 1) ? (filesize * 1024).ToString("0.##") + "MB" : filesize.ToString("0.##") + "GB",
                    Path       = applicationPath,
                };

                ApplicationLibraryData.Add(data);
            }
        }

        private static byte[] GetResourceBytes(string resourceName)
        {
            Stream resourceStream    = Assembly.GetCallingAssembly().GetManifestResourceStream(resourceName);
            byte[] resourceByteArray = new byte[resourceStream.Length];

            resourceStream.Read(resourceByteArray);

            return resourceByteArray;
        }

        private static IFileSystem GetControlFs(PartitionFileSystem Pfs)
        {
            Nca controlNca = null;

            // Add keys to keyset if needed
            foreach (DirectoryEntryEx ticketEntry in Pfs.EnumerateEntries("/", "*.tik"))
            {
                Result result = Pfs.OpenFile(out IFile ticketFile, ticketEntry.FullPath, OpenMode.Read);

                if (result.IsSuccess())
                {
                    Ticket ticket = new Ticket(ticketFile.AsStream());

                    KeySet.ExternalKeySet.Add(new RightsId(ticket.RightsId), new AccessKey(ticket.GetTitleKey(KeySet)));
                }
            }

            // Find the Control NCA and store it in variable called controlNca
            foreach (DirectoryEntryEx fileEntry in Pfs.EnumerateEntries("/", "*.nca"))
            {
                Pfs.OpenFile(out IFile ncaFile, fileEntry.FullPath, OpenMode.Read).ThrowIfFailure();

                Nca nca = new Nca(KeySet, ncaFile.AsStorage());

                if (nca.Header.ContentType == NcaContentType.Control)
                {
                    controlNca = nca;
                }
            }

            // Return the ControlFS
            return controlNca.OpenFileSystem(NcaSectionType.Data, IntegrityCheckLevel.None);
        }

        private static (bool, string, string) GetMetadata(string TitleId)
        {
            string metadataFolder = Path.Combine(new VirtualFileSystem().GetBasePath(), "games", TitleId, "gui");
            string metadataFile   = Path.Combine(metadataFolder, "metadata.json");

            IJsonFormatterResolver resolver = CompositeResolver.Create(new[] { StandardResolver.AllowPrivateSnakeCase });

            if (!File.Exists(metadataFile))
            {
                Directory.CreateDirectory(metadataFolder);

                AppMetadata = new ApplicationMetadata
                {
                    Fav        = false,
                    TimePlayed = 0,
                    LastPlayed = "Never"
                };

                byte[] saveData = JsonSerializer.Serialize(AppMetadata, resolver);
                File.WriteAllText(metadataFile, Encoding.UTF8.GetString(saveData, 0, saveData.Length).PrettyPrintJson());
            }

            using (Stream stream = File.OpenRead(metadataFile))
            {
                AppMetadata = JsonSerializer.Deserialize<ApplicationMetadata>(stream, resolver);
            }

            string timePlayed;

            if (AppMetadata.TimePlayed < SecondsPerMinute)
            {
                timePlayed = $"{AppMetadata.TimePlayed}s";
            }
            else if (AppMetadata.TimePlayed < SecondsPerHour)
            {
                timePlayed = $"{Math.Round(AppMetadata.TimePlayed / SecondsPerMinute, 2, MidpointRounding.AwayFromZero)} mins";
            }
            else if (AppMetadata.TimePlayed < SecondsPerDay)
            {
                timePlayed = $"{Math.Round(AppMetadata.TimePlayed / SecondsPerHour, 2, MidpointRounding.AwayFromZero)} hrs";
            }
            else
            {
                timePlayed = $"{Math.Round(AppMetadata.TimePlayed / SecondsPerDay, 2, MidpointRounding.AwayFromZero)} days";
            }

            return (AppMetadata.Fav, timePlayed, AppMetadata.LastPlayed);
        }

        private static byte[] NspOrXciIcon(string applicationPath)
        {
            if (Path.GetExtension(applicationPath) == ".xci")
            {
                return RyujinxXciIcon;
            }
            else
            {
                return RyujinxNspIcon;
            }
        }
    }
}
