﻿/*
 * CBFS Connect 2024 .NET Edition - Sample Project
 *
 * This sample project demonstrates the usage of CBFS Connect in a 
 * simple, straightforward way. It is not intended to be a complete 
 * application. Error handling and other checks are simplified for clarity.
 *
 * www.callback.com/cbfsconnect
 *
 * This code is subject to the terms and conditions specified in the 
 * corresponding product license agreement which outlines the authorized 
 * usage and restrictions.
 * 
 */

//
// Uncomment the DISK_QUOTAS_SUPPORTED define, if you want to support disk quotas.
// Additionally, you need to add to the Folder Drive's project a reference 
// to the "Microsoft Disk Quota" COM DLL.
//#define DISK_QUOTAS_SUPPORTED

#define EXECUTE_IN_DEFAULT_APP_DOMAIN

using System;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Text;
using System.Net;
using System.Text.Json;
using System.Net.Http;
using System.Text.Json.Serialization;







#if DISK_QUOTAS_SUPPORTED
using DiskQuotaTypeLibrary;
#endif

using callback.CBFSConnect;

namespace WorldComputer.Simulator
{
    public class VDrive
    {
        #region Variables and Types
        private static CBFS? mCBFSConn;
        internal static string mWorldComputerVirturalDiskID = "00000000000000000000000000000000";
        internal static string mWorldComputerVirtualDiskVolumeID = null!;
        private static string mWorldComputerVirtualDiskDefinition = null!;
        private static int numClusters = 0;
        private static int numReplicas = 0;
        private static string mRootPath = "";
        private static string mClientDataPathTemplate = "";
        private static string mOptMountingPoint = "";
        private static string mOptIconPath = "";
        private static string mOptDrvPath = "";
        private static bool mOptLocal = false;
        private static bool mOptNetwork = false;
        private static UInt32 mOptPermittedProcessId = 0;
        private static string mOptPermittedProcessName = "";
        private static bool mEnumerateNamedStreamsEventSet = false;
        private static bool mGetFileNameByFileIdEventSet = false;
        private static bool mEnumerateHardLinksEventSet = false;
        private const int BUFFER_SIZE = 1024 * 1024; // 1 MB
        private const string mGuid = "{713CC6CE-B3E2-4fd9-838D-E28F558F6866}";
        private const string DEFAULT_CAB_FILE_PATH = "./cbfs.cab";
        private const int MAX_FILENAME_LENGTH = 260;
        //private static BlockManager mSetManager = null!;
        private static VDriveInfo vDriveInfo = null!;
        private static int[,] VDiskStorageArray = null!;
        private static bool mountFailed = false;
        //private static string clientUrl = null!;
        #endregion


        public static string Init(VDriveInfo vdriveinfo, string clientMountRoot , string clientMountDataDirectoryTemplate )
        {
            vDriveInfo = vdriveinfo;
            bool op = false;
            //var vDiskID = vDriveInfo.VDiskID.ToString("N").ToUpper();
            var vDiskID = vDriveInfo.VirtualDiskSessionToken;
            string[] args = new string[3];
            args[0] = vDiskID;
            args[1] = clientMountRoot;
            args[2] = new String(vDriveInfo.DriveLetter,1);

            if (args.Length < 3)
            {
                Usage();
                return null!;
            }

            if (HandleParameters(args) == 0)
            {
                // check the operation
                if (mOptDrvPath != "")
                {
                    //install the drivers
                    InstallDriver();
                    op = true;
                }
                if (mOptIconPath != "")
                {
                    //register the icon
                    InstallIcon();
                    op = true;
                }
                if (mOptMountingPoint != "" && mRootPath != "")
                {
                    mClientDataPathTemplate = clientMountDataDirectoryTemplate;
                    RunMounting();
                    if(mountFailed)
                    {
                        Program.DisplayUnsuccessfulWriteLine("*** ERROR:  This was an issue mounting the virtual disk.  Please try again.");
                    }
                    op = true;
                }
            }
            if (!op)
            {
                Program.DisplayUnsuccessfulWriteLine("Unrecognized combination of parameters passed. You need to either install the driver using -drv switch or specify the size of the disk and the mounting point name to create a virtual disk");
            }
            return mWorldComputerVirtualDiskDefinition;
        }

    
        private static void SetupCBFS()
        {
            CheckDriver();

            mCBFSConn = new CBFS();
            mCBFSConn.Initialize(mGuid);

            mCBFSConn.StorageCharacteristics = 0;

            #region Configure VDrive Properties
            mCBFSConn.Config("SectorSize=" + Math.Min(vDriveInfo.BlockSize, 4096).ToString());
            mCBFSConn.Config("ClusterSize=" + vDriveInfo.BlockSize.ToString());

            mCBFSConn.Config("MaxReadBlockSize=" + vDriveInfo.MaxReadBlockSize.ToString());
            mCBFSConn.Config("MaxWriteBlockSize=" +vDriveInfo.MaxWriteBlockSize.ToString());
            mCBFSConn.Config("CorrectAllocationSizes=false");
            mCBFSConn.Config("MaxWorkerThreadCount=100");
            mCBFSConn.Config("MinWorkerThreadCount=40");
            mCBFSConn.Config("FireSetFileSizeOnWrite=false");
            mCBFSConn.Config("FlushFileBeforeUnlock=true");
            mCBFSConn.Config("ForceFileClose=true");

            mCBFSConn.SerializeAccess = false;
            mCBFSConn.SerializeEvents = CBFSSerializeEvents.seOnMultipleThreads;
            mCBFSConn.FileCache = CBFSFileCaches.fcNone;
            mCBFSConn.StorageCharacteristics = 16; //CBFS_REMOVABLE_MEDIA;
            mCBFSConn.StorageType = 0; //stDisk
            mCBFSConn.FireAllOpenCloseEvents = false;
            mCBFSConn.FileSystemName = "NTFS";
            mCBFSConn.UseCaseSensitiveFileNames = false;
            mCBFSConn.UseShortFileNames = false;
            ////mCBFSConn.UseFileCreationFlags = true;  ??
            #endregion

            mCBFSConn.OnMount += new CBFS.OnMountHandler(CBMount);
            mCBFSConn.OnUnmount += new CBFS.OnUnmountHandler(CBUnmount);
            mCBFSConn.OnGetVolumeSize += new CBFS.OnGetVolumeSizeHandler(CBGetVolumeSize);
            mCBFSConn.OnGetVolumeLabel += new CBFS.OnGetVolumeLabelHandler(CBGetVolumeLabel);
            mCBFSConn.OnSetVolumeLabel += new CBFS.OnSetVolumeLabelHandler(CBSetVolumeLabel);
            mCBFSConn.OnGetVolumeId += new CBFS.OnGetVolumeIdHandler(CBGetVolumeId);
            mCBFSConn.OnCreateFile += new CBFS.OnCreateFileHandler(CBCreateFile);
            mCBFSConn.OnOpenFile += new CBFS.OnOpenFileHandler(CBOpenFile);
            mCBFSConn.OnCloseFile += new CBFS.OnCloseFileHandler(CBCloseFile);
            mCBFSConn.OnGetFileInfo += new CBFS.OnGetFileInfoHandler(CBGetFileInfo);
            mCBFSConn.OnEnumerateDirectory += new CBFS.OnEnumerateDirectoryHandler(CBEnumerateDirectory);
            mCBFSConn.OnCloseDirectoryEnumeration += new CBFS.OnCloseDirectoryEnumerationHandler(CBCloseDirectoryEnumeration);
            mCBFSConn.OnSetAllocationSize += new CBFS.OnSetAllocationSizeHandler(CBSetAllocationSize);
            mCBFSConn.OnSetFileSize += new CBFS.OnSetFileSizeHandler(CBSetFileSize);
            mCBFSConn.OnSetFileAttributes += new CBFS.OnSetFileAttributesHandler(CBSetFileAttributes);
            mCBFSConn.OnCanFileBeDeleted += new CBFS.OnCanFileBeDeletedHandler(CBCanFileBeDeleted);
            mCBFSConn.OnDeleteFile += new CBFS.OnDeleteFileHandler(CBDeleteFile);
            mCBFSConn.OnRenameOrMoveFile += new CBFS.OnRenameOrMoveFileHandler(CBRenameOrMoveFile);
            mCBFSConn.OnReadFile += new CBFS.OnReadFileHandler(CBReadFile);
            mCBFSConn.OnWriteFile += new CBFS.OnWriteFileHandler(CBWriteFile);
            mCBFSConn.OnIsDirectoryEmpty += new CBFS.OnIsDirectoryEmptyHandler(CBIsDirectoryEmpty);

            mCBFSConn.UseAlternateDataStreams = false;
            mCBFSConn.OnEnumerateNamedStreams += new CBFS.OnEnumerateNamedStreamsHandler(CBEnumerateNamedStreams);
            mEnumerateNamedStreamsEventSet = false;
            mCBFSConn.OnCloseNamedStreamsEnumeration += new CBFS.OnCloseNamedStreamsEnumerationHandler(CBCloseNamedStreamsEnumeration);
            mCBFSConn.OnEjected += new CBFS.OnEjectedHandler(CBEjectStorage);


            // Uncomment in order to support file security.
            //mCBFSConn.UseWindowsSecurity = true;
            //mCBFSConn.OnGetFileSecurity += new CBFS.OnGetFileSecurityHandler(CBGetFileSecurity);
            //mCBFSConn.OnSetFileSecurity += new CBFS.OnSetFileSecurityHandler(CBSetFileSecurity);
            //mCBFSConn.FileSystemName = "NTFS";

            // Uncomment in order to support file IDs (necessary for NFS sharing) or
            // hard links (the CBEnumerateHardLinks and CBCreateHardLink callbacks).
            //mCBFSConn.OnGetFileNameByFileId += new CBFS.OnGetFileNameByFileIdHandler(CBGetFileNameByFileId);
            //mGetFileNameByFileIdEventSet = true;
            //mCBFSConn.UseFileIds = true;

            // Uncomment in order to support FSCTLs.
            //mCBFSConn.OnFsctl += new CBFS.OnFsctlHandler(CBFsctl);

            // Uncomment in order to support hard links (several names for the same file,
            // it works only if the CBGetFileNameByFileId callback is defined too). 
            //mCBFSConn.UseHardLinks = true;
            //mCBFSConn.OnEnumerateHardLinks += new CBFS.OnEnumerateHardLinksHandler(CBEnumerateHardLinks);
            //mEnumerateHardLinksEventSet = true;
            //mCBFSConn.OnCloseHardLinksEnumeration += new CBFS.OnCloseHardLinksEnumerationHandler(CBCloseHardLinksEnumeration);
            //mCBFSConn.OnCreateHardLink += new CBFS.OnCreateHardLinkHandler(CBCreateHardLink);
            //mCBFSConn.FileSystemName = "NTFS";

            // Uncomment in order to support reparse points (symbolic links, mounting points, etc.) support. Also required for NFS sharing
            //mCBFSConn.UseReparsePoints = true;
            //mCBFSConn.OnSetReparsePoint += new CBFS.OnSetReparsePointHandler(CBSetReparsePoint);
            //mCBFSConn.OnGetReparsePoint += new CBFS.OnGetReparsePointHandler(CBGetReparsePoint);
            //mCBFSConn.OnDeleteReparsePoint += new CBFS.OnDeleteReparsePointHandler(CBDeleteReparsePoint);
            //mCBFSConn.FileSystemName = "NTFS";

#if DISK_QUOTAS_SUPPORTED
            mCBFSConn.UseDiskQuotas = true;
            mCBFSConn.OnQueryQuotas += new CBFS.OnQueryQuotasHandler(CBQueryQuotas);
            mCBFSConn.OnSetQuotas += new CBFS.OnSetQuotasHandler(CBSetQuotas);
            mCBFSConn.OnCloseQuotasEnumeration += new CBFS.OnCloseQuotasEnumerationHandler(CBCloseQuotasEnumeration);

            // This feature works only if the CBGetFileNameByFileId callback is defined too
            if (!mGetFileNameByFileIdEventSet)
            {
                mCBFSConn.OnGetFileNameByFileId += new CBFS.OnGetFileNameByFileIdHandler(CBGetFileNameByFileId);
                mGetFileNameByFileIdEventSet = true;
            }
            //mCBFSConn.FileSystemName = "NTFS";
#endif //DISK_QUOTAS_SUPPORTED


        }

        static int RunMounting()
        {
            SetupCBFS();
            if (mCBFSConn != null)
            {
                // attempt to create the storage
                try
                {
                    mCBFSConn.CreateStorage();
                }
                catch (CBFSConnectException e)
                {
                    Program.DisplayUnsuccessfulWriteLine($"Failed to create storage, error {e.Code} ({e.Message})" );
                    return e.Code;
                }

                // Mount a "media"
                try
                {
                    mCBFSConn.MountMedia(0);  // NOTE:  fires CBMount callback event handler
                }
                catch (CBFSConnectException e)
                {
                    Program.DisplayUnsuccessfulWriteLine($"Failed to mount media, error {e.Code} ({e.Message})");
                    return e.Code;
                }
                if( mountFailed )
                {
                    return -1;
                }
                // Add a mounting point
                try
                {
                    if (mOptMountingPoint.Length == 1)
                        mOptMountingPoint = mOptMountingPoint + ":";
                    AddMountingPoint(mOptMountingPoint);
                }
                catch (CBFSConnectException e)
                {
                    Program.DisplayUnsuccessfulWriteLine($"Failed to add mounting point, error {e.Code} ({e.Message})");
                    return e.Code;
                }

                // Set the icon
                if (mCBFSConn.IsIconRegistered("sample_icon"))
                    mCBFSConn.SetIcon("sample_icon");

                #region Display VDisk Clusters
                string[] clusters = mWorldComputerVirtualDiskDefinition.Substring(32).Split('|');
                numClusters = clusters.Length;
                numReplicas = clusters[0].Split(',').Length;
                Program.DisplaySuccessfulWriteLine($"");
                Program.DisplaySuccessfulWriteLine($"VDisk Stoage Array: ({clusters.Length},{numReplicas})");
                Program.DisplaySuccessfulWriteLine($"");
                VDiskStorageArray = new int[numClusters, numReplicas];
                //foreach ( var c in clusters)
                for(int c = 0; c < numClusters; c++)
                {
                    string[] replicas = clusters[c].Split(",");
                    Program.DisplaySuccessfulWrite($"[ ");
                    for ( int r = 0; r < numReplicas; r++)
                    {
                        string[] node = replicas[r].Split(":");
                        VDiskStorageArray[c, r] = Program.ComputerNodeNumberFromPort(int.Parse(node[1]));
                        Program.DisplaySuccessfulWrite($"{ Program.ComputerNodeNumberFromPort(int.Parse(node[1])).ToString().PadLeft(3)}");
                        if (r < numReplicas - 1) Program.DisplaySuccessfulWrite(", ");
                    }
                    Program.DisplaySuccessfulWriteLine($"   ]");
                }
                #endregion

                #region Initialize SetManager with vDisk and vDrive information
                //mSetManager = new BlockManager(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), numClusters, numReplicas, VDiskStorageArray, vDriveInfo.BlockSize, mClientDataPathTemplate);
                #endregion 
                Program.DisplaySuccessfulWriteLine($"Mounting complete. {mOptMountingPoint} is now accessible." );

                while (true)
                {
                    Program.DisplaySuccessfulWriteLine("To unmount the drive and exit, press Enter");
                    Console.ReadLine();

                    // remove a mounting point
                    try
                    {
                        mCBFSConn.RemoveMountingPoint(-1, mOptMountingPoint, 0, 0);


                    }
                    catch (CBFSConnectException e)
                    {
                        Program.DisplayUnsuccessfulWriteLine($"Failed to remove mounting point, error {e.Code} ({e.Message})");
                        continue;
                    }

                    // delete a storage
                    try
                    {
                        mCBFSConn.DeleteStorage(true);  // NOTE:  fires CBUnmount callback event handler if fource unmount true flag is passed
                        Program.DisplaySuccessfulWrite("Disk unmounted.");
                        return 0;
                    }
                    catch (CBFSConnectException e)
                    {
                        Program.DisplaySuccessfulWriteLine($"Failed to delete storage, error {e.Code} ({e.Message})");
                        continue;
                    }
                }
            }
            else
            {
                throw new Exception("Callback FileSystem object not initialized.");
            }
        }


        #region Helper Methods
        #region CBFS Helpers
        static int HandleParameters(string[] args)
        {
            int i = 0;
            string curParam;
            int nonDashParam = 0;

            while (i < args.Length)
            {
                curParam = args[i];
                if (string.IsNullOrEmpty(curParam))
                    break;

                if (nonDashParam == 0)
                {
                    if (curParam[0] == '-')
                    {
                        // icon path
                        if (curParam == "-i")
                        {
                            if (i + 1 < args.Length)
                            {
                                mOptIconPath = ConvertRelativePathToAbsolute(args[i + 1]);
                                if (!string.IsNullOrEmpty(mOptIconPath))
                                {
                                    i += 2;
                                    continue;
                                }
                            }
                            Program.DisplayUnsuccessfulWriteLine("-i switch requires the valid path to the .ico file with icons as the next parameter.");
                            return 1;
                        }
                        else
                        // user-local disk
                        if (curParam == "-lc")
                        {
                            mOptLocal = true;
                        }
                        else
                        // network disk
                        if (curParam == "-n")
                        {
                            mOptNetwork = true;
                        }
                        else
                        // permitted process
                        if (curParam == "-ps")
                        {
                            if (i + 1 < args.Length)
                            {
                                curParam = args[i + 1];
                                if (!UInt32.TryParse(curParam, out mOptPermittedProcessId))
                                    mOptPermittedProcessName = curParam;
                                i += 2;
                                continue;
                            }
                            Program.DisplayUnsuccessfulWriteLine("Process Id or process name must follow the -ps switch. Process Id must be a positive integer, and a process name may contain an absolute path or just the name of the executable module.");
                            return 1;
                        }
                        else
                        // driver path
                        if (curParam == "-drv")
                        {
                            if (i + 1 < args.Length)
                            {
                                mOptDrvPath = ConvertRelativePathToAbsolute(args[i + 1]);
                                if (!string.IsNullOrEmpty(mOptDrvPath) && (Path.GetExtension(mOptDrvPath) == ".cab"))
                                {
                                    i += 2;
                                    continue;
                                }
                            }
                            Program.DisplayUnsuccessfulWriteLine("-drv switch requires the valid path to the .cab file with drivers as the next parameter.");
                            return 1;
                        }
                    }
                    else
                    {
                        mWorldComputerVirturalDiskID = curParam;
                        i++;
                        nonDashParam += 1;
                        continue;
                    }
                }
                else if (nonDashParam == 1)
                {
                    mRootPath = ConvertRelativePathToAbsolute(curParam);
                    if (string.IsNullOrEmpty(mRootPath))
                    {
                        Program.DisplayUnsuccessfulWriteLine($"The folder '{curParam}', specified as the one to map, doesn't seem to exist.");
                        return 1;
                    }

                    i++;
                    nonDashParam += 1;
                    continue;
                }
                else
                {
                    if (curParam.Length == 1)
                    {
                        curParam += ":";
                    }
                    mOptMountingPoint = ConvertRelativePathToAbsolute(curParam, true);
                    if (string.IsNullOrEmpty(mOptMountingPoint))
                    {
                        Program.DisplayUnsuccessfulWriteLine("Error: Invalid Mounting Point Path");
                        return 1;
                    }
                    break;
                }
                i++;
            }
            return 0;
        }


        static void Usage()
        {
            Program.DisplaySuccessfulWriteLine("Usage: WCVDrive.exe <vDisk ID> <mapped folder> <mounting point>");
            Program.DisplaySuccessfulWriteLine("");
            Program.DisplaySuccessfulWriteLine("For example:  WCVDrive.exe C:\\temp Z:");
        }



        private static void AskForReboot(bool isInstall)
        {
            Program.DisplaySuccessfulWriteLine("Warning: System restart is needed in order to " + (isInstall ? "install" : "uninstall") + " the drivers.\rPlease, reboot your computer now.");
        }

        private static bool isWriteRightRequested(CBFileAccess access)
        {
            CBFileAccess writeRight = CBFileAccess.file_write_data |
                                      CBFileAccess.file_append_data |
                                      CBFileAccess.file_write_ea |
                                      CBFileAccess.file_write_attributes;
            return (access & writeRight) != 0;
        }

        private static void AddMountingPoint(string mp)
        {
            try
            {
                int flags = (mOptLocal ? Constants.STGMP_LOCAL : 0);

                if (mOptNetwork && mp.Contains(";")) //Assume it's a network mounting point for demo purposes
                {
                    mCBFSConn.AddMountingPoint(mp, Constants.STGMP_NETWORK | Constants.STGMP_NETWORK_ALLOW_MAP_AS_DRIVE | flags, 0);
                    mp = mp.Substring(0, mp.IndexOf(";"));
                }
                else
                    mCBFSConn.AddMountingPoint(mp, Constants.STGMP_MOUNT_MANAGER | flags, 0);

                Process.Start("explorer.exe", mp);
            }
            catch (CBFSConnectException err)
            {
                Program.DisplayUnsuccessfulWriteLine("Error: " + err.Message);
            }
        }

        private static int ExceptionToErrorCode(Exception ex)
        {
            if (ex is FileNotFoundException)
                return ERROR_FILE_NOT_FOUND;
            else if (ex is DirectoryNotFoundException)
                return ERROR_PATH_NOT_FOUND;
            else if (ex is DriveNotFoundException)
                return ERROR_INVALID_DRIVE;
            else if (ex is OperationCanceledException)
                return ERROR_OPERATION_ABORTED;
            else if (ex is UnauthorizedAccessException)
                return ERROR_ACCESS_DENIED;
            else if (ex is Win32Exception)
                return ((Win32Exception)ex).NativeErrorCode;
            else if (ex is IOException && ex.InnerException is Win32Exception)
                return ((Win32Exception)ex.InnerException).NativeErrorCode;
            else
                return ERROR_INTERNAL_ERROR;
        }

        private static bool isWriteRightForAttrRequested(CBFileAccess access)
        {
            CBFileAccess writeRight = CBFileAccess.file_write_data |
                                      CBFileAccess.file_append_data |
                                      CBFileAccess.file_write_ea;
            return ((access & writeRight) == 0) && ((access & CBFileAccess.file_write_attributes) != 0);
        }
        private static bool isReadRightForAttrOrDeleteRequested(CBFileAccess access)
        {
            CBFileAccess readRight = CBFileAccess.file_read_data | CBFileAccess.file_read_ea;
            CBFileAccess readAttrOrDeleteRight = CBFileAccess.file_read_attributes | CBFileAccess.Delete;

            return ((access & readRight) == 0) && ((access & readAttrOrDeleteRight) != 0);
        }



        private static void CheckDriver()
        {
            CBFS disk = new CBFS();

            int status = disk.GetDriverStatus(mGuid, Constants.MODULE_DRIVER);

            if (status != 0)
            {
                string strStat;

                switch (status)
                {
                    case (int)CBDriverState.SERVICE_CONTINUE_PENDING:
                        strStat = "continue is pending";
                        break;
                    case (int)CBDriverState.SERVICE_PAUSE_PENDING:
                        strStat = "pause is pending";
                        break;
                    case (int)CBDriverState.SERVICE_PAUSED:
                        strStat = "is paused";
                        break;
                    case (int)CBDriverState.SERVICE_RUNNING:
                        strStat = "is running";
                        break;
                    case (int)CBDriverState.SERVICE_START_PENDING:
                        strStat = "is starting";
                        break;
                    case (int)CBDriverState.SERVICE_STOP_PENDING:
                        strStat = "is stopping";
                        break;
                    case (int)CBDriverState.SERVICE_STOPPED:
                        strStat = "is stopped";
                        break;
                    default:
                        strStat = "in undefined state";
                        break;
                }

                long version = disk.GetModuleVersion(mGuid, Constants.MODULE_DRIVER);
                Program.DisplaySuccessfulWriteLine(string.Format("Driver Status: (ver {0}.{1}.{2}.{3}) installed, service {4}", version >> 48, (version >> 32) & 0xFFFF, (version >> 16) & 0xFFFF, version & 0xFFFF, strStat));
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine("Driver not installed. Would you like to install it now? (y/n): ");
                string input = Console.ReadLine()!;
                if (input.ToLower().StartsWith("y"))
                {
                    //Install the driver
                    InstallDriver();
                }
            }
        }

        static int InstallDriver()
        {
            int drvReboot = 0;
            CBFS disk = new CBFS();

            if (mOptDrvPath == "")
            {
                mOptDrvPath = DEFAULT_CAB_FILE_PATH;
            }

            Program.DisplaySuccessfulWriteLine($"Installing the drivers from {mOptDrvPath}");
            try
            {
                drvReboot = disk.Install(mOptDrvPath, mGuid, null, Constants.MODULE_DRIVER | Constants.MODULE_HELPER_DLL, Constants.INSTALL_REMOVE_OLD_VERSIONS);
            }
            catch (CBFSConnectException err)
            {
                if (err.Code == ERROR_PRIVILEGE_NOT_HELD)
                    Program.DisplayUnsuccessfulWriteLine("Error: Installation requires administrator rights. Run the app as administrator");
                else
                    Program.DisplayUnsuccessfulWriteLine($"Error: Drivers are not installed, error code {err.Code} ({err.Message})");
                return err.Code;
            }
            Program.DisplaySuccessfulWriteLine("Drivers installed successfully");
            if (drvReboot != 0)
                Program.DisplaySuccessfulWriteLine("Reboot is required by the driver installation routine");
            return 0;
        }

        static int InstallIcon()
        {
            bool reboot = false;
            CBFS disk = new CBFS();
            Program.DisplaySuccessfulWriteLine($"Installing the icon from {mOptIconPath}");
            try
            {
                reboot = disk.RegisterIcon(mOptIconPath, mGuid, "sample_icon");
            }
            catch (CBFSConnectException err)
            {
                if (err.Code == ERROR_PRIVILEGE_NOT_HELD)
                    Program.DisplayUnsuccessfulWriteLine("Error: Icon registration requires administrator rights. Run the app as administrator");
                else
                    Program.DisplayUnsuccessfulWriteLine($"Error: Icon not registered, error code {err.Code} ({err.Message})");
                return err.Code;
            }
            Program.DisplaySuccessfulWriteLine("Drivers installed successfully");
            if (reboot)
                Program.DisplaySuccessfulWriteLine("Reboot is required by the icon registration routine");
            return 0;
        }

       
        private static string ConvertRelativePathToAbsolute(string path, bool acceptMountingPoint = false)
        {
            string res = null;
            if (!string.IsNullOrEmpty(path))
            {
                res = path;

                // Linux-specific case of using a home directory
                if (path.Equals("~") || path.StartsWith("~/"))
                {
                    string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    if (path.Equals("~") || path.Equals("~/"))
                        return homeDir;
                    else
                        return Path.Combine(homeDir, path.Substring(2));
                }
                int semicolonCount = path.Split(';').Length - 1;
                bool isNetworkMountingPoint = semicolonCount == 2;
                string remainedPath = "";
                if (isNetworkMountingPoint)
                {
                    if (!acceptMountingPoint)
                    {
                        Program.DisplayUnsuccessfulWriteLine($"The path '{path}' format cannot be equal to the Network Mounting Point");
                        return "";
                    }
                    int pos = path.IndexOf(';');
                    if (pos != path.Length - 1)
                    {
                        res = path.Substring(0, pos);
                        if (String.IsNullOrEmpty(res))
                        {
                            return path;
                        }
                        remainedPath = path.Substring(pos);
                    }
                }

                if (IsDriveLetter(res))
                {
                    if (!acceptMountingPoint)
                    {
                        Program.DisplayUnsuccessfulWriteLine($"The path '{res}' format cannot be equal to the Drive Letter");
                        return "";
                    }
                    return path;
                }

                if (!Path.IsPathRooted(res))
                {
                    res = Path.GetFullPath(res);

                    return res + remainedPath;
                }
            }
            return path;
        }

        private static bool IsDriveLetter(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            char c = path[0];
            if ((char.IsLetter(c) && path.Length == 2 && path[1] == ':'))
                return true;
            else
                return false;
        }
        #endregion 

        #region World Computer Api helpers
        internal static string WCVirtualDiskMountApi( string vdiskID )  // NOTE:  Also called by Cluster /CREATE
        {
            string mountResult = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskMount*/ 722},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""VirtualDiskID"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{vdiskID}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""BlockSize"",
                                        ""T"" : ""System.UInt32"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{vDriveInfo.BlockSize}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Retrieve result of call
                var responseJson = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseJson);
                var element = doc.RootElement;
                //mWorldComputerVirtualDiskDefinition = element.GetProperty("O")[0].GetProperty("V").GetString()!;
                //mWorldComputerVirtualDiskVolumeID = mWorldComputerVirtualDiskDefinition.Substring(0, 32);
                mountResult = element.GetProperty("O")[0].GetProperty("V").GetString()!;
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling VirtualDiskMount() - response.StatusCode={response.StatusCode}");
            }
            return mountResult;
        }

        internal static void WCVirtualDiskUnmountApi() // NOTE:  Also called by Cluster /CREATE
        {
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskUnMount*/ 723},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""VolumeID"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{mWorldComputerVirtualDiskVolumeID}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode != HttpStatusCode.OK)
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling VirtualDiskUnmount() - response.StatusCode={response.StatusCode}");
            }
            else
            {
                mWorldComputerVirtualDiskVolumeID = null!;
            }
        }

        private static string WCVirtualDiskVolumeMetaDataOperationApi( string base64Operation )
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskVolumeMetaDataOperation*/ 724},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Operation"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{base64Operation}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Retrieve result of call
                var responseJson = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseJson);
                var element = doc.RootElement;
                result = element.GetProperty("O")[0].GetProperty("V").GetString()!;
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling WCVirtualDiskVolumeMetaDataOperationApi() - response.StatusCode={response.StatusCode}");
            }
            return result;
        }


        internal static string WCVirtualDiskVolumeDataOperationApi(string base64Operation)  
        {
            string result = null!;
            var json = @$"
            {{
                ""V"" : 1,
                ""F"" : 0,
                ""A"" : {/*(int)ApiIdentifier.VirtualDiskVolumeDataOperation*/ 725},
                ""I"" : [{{
                            ""D"" : {{
                                        ""N"" : ""UserSessionToken"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{Program.WC_OSUSER_SESSION_TOKEN}"",
                            ""N""  : false
                        }},{{
                            ""D"" : {{
                                        ""N"" : ""Operation"",
                                        ""T"" : ""System.String"",
                                        ""MV"" : false,
                                        ""IN"" : false
                                    }},
                            ""V""  : ""{base64Operation}"",
                            ""N""  : false
                        }}]
            }}";

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = Program.UnoSysApiConnection.PostAsync(content).Result;
            if (response.StatusCode == HttpStatusCode.OK)
            {
                // Retrieve result of call
                var responseJson = response.Content.ReadAsStringAsync().Result;
                using var doc = JsonDocument.Parse(responseJson);
                var element = doc.RootElement;
                result = element.GetProperty("O")[0].GetProperty("V").GetString()!;
            }
            else
            {
                Program.DisplayUnsuccessfulWriteLine($"Error calling WCVirtualDiskVolumeDataOperationApi() - response.StatusCode={response.StatusCode}");
            }
            return result;
        }
        #endregion 
        #endregion

        #region Event handlers
        private static void CBMount(object sender, CBFSMountEventArgs e)
        {
            Debug.Print($"CBMount(ENTER) vDiskID={mWorldComputerVirturalDiskID}");

            try
            {
                var mountResult = WCVirtualDiskMountApi(mWorldComputerVirturalDiskID);
                mWorldComputerVirtualDiskDefinition = mountResult;
                mWorldComputerVirtualDiskVolumeID = mWorldComputerVirtualDiskDefinition.Substring(0, 32);
            }
            catch (Exception ex)
            {
                mountFailed = true;
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            Debug.Print($"CBMount(LEAVE) - VolumeID={mWorldComputerVirtualDiskVolumeID}");
        }

        private static void CBUnmount(object sender, CBFSUnmountEventArgs e)
        {
            Debug.Print($"CBUnmount(ENTER) vDiskID={mWorldComputerVirturalDiskID}");

            try
            {
                WCVirtualDiskUnmountApi();
            }
            catch (Exception ex)
            {

                e.ResultCode = ExceptionToErrorCode(ex);
            }
            Debug.Print($"CBUnmount(LEAVE)");
        }

        private static void CBGetVolumeSize(object sender, CBFSGetVolumeSizeEventArgs e)
        {
            //Debug.Print($"CBGetVolumeSize(ENTER)");

            //CBFS fs = sender as CBFS;
            ulong freeBytesAvailable;
            ulong totalNumberOfBytes;
            ulong totalNumberOfFreeBytes;

            try
            {
                if (GetDiskFreeSpaceEx(mRootPath, out freeBytesAvailable, out totalNumberOfBytes, out totalNumberOfFreeBytes) != true)
                {
                    freeBytesAvailable = 0;
                    totalNumberOfBytes = 0;
                    totalNumberOfFreeBytes = 0;
                }

#if !DISK_QUOTAS_SUPPORTED
                e.AvailableSectors = (long)totalNumberOfFreeBytes / SECTOR_SIZE;
                e.TotalSectors = (long)totalNumberOfBytes / SECTOR_SIZE;
#else
                SafeFileHandle token;
                WindowsIdentity wi;
                DIDiskQuotaUser user;
                long RemainingQuota;

                DiskQuotaControlClass ctrl;

                token = new SafeFileHandle(new IntPtr(mCBFSConn.GetOriginatorToken()), true);
                wi = new WindowsIdentity(token.DangerousGetHandle());
                token.Close();

                ctrl = new DiskQuotaControlClass();
                ctrl.Initialize(mRootPath[0] + ":\\", true);
                ctrl.UserNameResolution = UserNameResolutionConstants.dqResolveSync;

                user = ctrl.FindUser(wi.User.Value);

                if ((ulong)user.QuotaLimit < totalNumberOfBytes)
                    e.TotalSectors = (long)user.QuotaLimit / SECTOR_SIZE;
                else
                    e.TotalSectors = (long)totalNumberOfBytes / SECTOR_SIZE;

                if ((ulong)user.QuotaLimit <= (ulong)user.QuotaUsed)
                    RemainingQuota = 0;
                else
                    RemainingQuota = (long)(user.QuotaLimit - user.QuotaUsed);

                if ((ulong)RemainingQuota < totalNumberOfFreeBytes)
                    e.AvailableSectors = RemainingQuota / SECTOR_SIZE;
                else
                    e.AvailableSectors = (long)totalNumberOfFreeBytes / SECTOR_SIZE;
#endif //DISK_QUOTAS_SUPPORTED
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBGetVolumeLabel(object sender, CBFSGetVolumeLabelEventArgs e)
        {
            Debug.Print($"CBGetVolumeLabel(ENTER)");
            e.Buffer = "WorldComputer Drive";
        }

        private static void CBSetVolumeLabel(object sender, CBFSSetVolumeLabelEventArgs e)
        {
            Debug.Print($"CBSetVolumeLabel(ENTER)");
        }

        private static void CBGetVolumeId(object sender, CBFSGetVolumeIdEventArgs e)
        {
            Debug.Print($"CBGetVolumeId(ENTER)");
            e.VolumeId = 0x12345678;
        }

        private static void CBCreateFile(object sender, CBFSCreateFileEventArgs e)
        {
            //Debug.Print($"CBCreateFile(ENTER) - fileName={e.FileName} NTCreateDisposition={e.NTCreateDisposition}");
            //bool isDirectory = false;
            //bool IsStream = e.FileName.Contains(":");

            try
            {
                if( e.FileName.Length > MAX_FILENAME_LENGTH )
                {
                    e.ResultCode = ERROR_FILENAME_TOO_LONG;
                    return;
                }
                FileStreamContext Ctx = new FileStreamContext();
                
               /* isDirectory = (e.Attributes & (uint)CBFileAttributes.Directory) != 0;

                if (isDirectory)
                {
                    if (IsStream)
                    {
                        e.ResultCode = ERROR_DIRECTORY;
                        return;
                    }

                    Directory.CreateDirectory(mRootPath + e.FileName);
                }
                else
                {
                    //
                    // if we need to support alternative data streams, we must use
                    // native methods to open file stream, as it is no support for ADS in .NET
                    //
                    if (mEnumerateNamedStreamsEventSet && IsStream)
                    {
                        SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                            GENERIC_READ | GENERIC_WRITE,
                            FileShare.ReadWrite | FileShare.Delete,
                            IntPtr.Zero,
                            FileMode.Create,
                            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT | FILE_ATTRIBUTE_ARCHIVE,
                            IntPtr.Zero
                            );

                        if (hFile.IsInvalid)
                        {
                            throw new IOException("Could not create file stream.", new Win32Exception(Marshal.GetLastWin32Error()));
                        }

                        Ctx.hStream = new FileStream(hFile, FileAccess.ReadWrite);
                    }
                    else
                        Ctx.hStream = new FileStream(mRootPath + e.FileName, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);

                    if (!IsStream)
                    {
                        File.SetAttributes(mRootPath + e.FileName, (FileAttributes)(e.Attributes & 0xFFFF)); // Attributes contains creation flags as well, which we need to strip
                        Ctx.Attributes = (e.Attributes & 0xFFFF);
                    }
                }
               */
                e.FileContext = GCHandle.ToIntPtr(GCHandle.Alloc(Ctx));
                Ctx.IncrementCounter();
                Ctx.NTCreateDisposition = e.NTCreateDisposition;  // RdS

                #region Submit FILESYSTEM_CREATE_FILE World Computer event
                //if ((Ctx != null && Ctx.hStream != null) || isDirectory)
                //{
                Ctx.FileID = Guid.NewGuid();
                Ctx.Attributes = e.Attributes;
                Ctx.FileLength = 0;
                FILECREATE_Operation operation = new FILECREATE_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), Ctx.FileID,
                        e.FileName, e.DesiredAccess, (e.Attributes & 0xFFFF), e.ShareMode, e.NTCreateDisposition, e.NTDesiredAccess );
                
                
                Debug.Print($"CBCreateFile(A) - FileID={Ctx.FileID} NTCreateDisposition={e.NTCreateDisposition}, IsDir={(Ctx.Attributes & (uint)0x10 /*CBFileAttributes.Directory*/) != 0} F={e.FileName}");
                WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                //}
                //else
                //{
                //    Debug.Print($"CBCreateFile(C) - fileName={e.FileName} *** hStream is null *** - NTCreateDisposition={e.NTCreateDisposition}");
                //}
                //NetworkCredentials network = new NetworkCredentials();
                //Address topic = new Address(0, 0, 4538064);
                #endregion 
            }
            catch (Exception ex)
            {
                Debug.Print($"CBCreateFile(ERROR) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            //Debug.Print($"CBCreateFile(LEAVE) - fileName={e.FileName} NTCreateDisposition={e.NTCreateDisposition}");
        }

        private static void CBCloseFile(object sender, CBFSCloseFileEventArgs e)
        {
            if (e.FileName == "/" || e.FileName == "\\")  // NOP when closing root directory
                return;

            try
            {
                if (e.FileContext != IntPtr.Zero)
                {
                    FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                    Debug.Print($"CBCloseFile(ENTER) - fileName={e.FileName} Ctx.FileID={Ctx.FileID}, IsDirectory={(Ctx.Attributes & (uint)0x10 /*CBFileAttributes.Directory*/) != 0}");
                    if (Ctx.OpenCount == 1)
                    {
                        // If we make it here we are closing a file.  If it is a "create" file then we need to call into the API
                        if( Ctx.NTCreateDisposition == 2 /*Create*/)
                        {
                            var operation = new FILECLOSE_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), e.FileName, Ctx.FileID, Ctx.CreateTime, 
                                            Ctx.LastAccessTime, Ctx.LastWriteTime, Ctx.ChangeTime, Ctx.Attributes, Ctx.EventOrigin, Ctx.FileLength);
                            WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                        }
                        
                        //if (Ctx.hStream != null) Ctx.hStream.Close();
                        GCHandle.FromIntPtr(e.FileContext).Free();
                    }
                    else
                    {
                        Ctx.DecrementCounter();
                    }
                }
            }
            catch (Exception ex)
            {
                //Debug.Print($"*** VDrive.CBCloseFile(ERROR) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            //Debug.Print($"CBCloseFile(LEAVE) - fileName={e.FileName} ");
        }


        private static void CBOpenFile(object sender, CBFSOpenFileEventArgs e)
        {
            //Debug.Print($"CBOpenFile(ENTER) - fileName={e.FileName} NTCreateDisposition={e.NTCreateDisposition}");
            FileStreamContext Ctx = null;
            //long FileId = -1;

            if (e.FileName == "/" || e.FileName == "\\")  // NOP when opening root directory
                return;
            try
            {

                if (e.FileContext != IntPtr.Zero)
                {
                    Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                    //if (Ctx.hStream != null && isWriteRightRequested((CBFileAccess)e.DesiredAccess))
                    //{
                    //    if (!Ctx.hStream.CanWrite) throw new UnauthorizedAccessException();
                    //}
                    Ctx.IncrementCounter();
                    //Debug.Print($"CBOpenFile(A) - fileName={Ctx.hStream.Name} NTCreateDisposition={e.NTCreateDisposition}");
                    return;
                }

                Ctx = new FileStreamContext();

                //if (IsQuotasIndexFile(e.FileName, ref FileId))
                //{
                //    //
                //    // at this point we emulate a
                //    // disk quotas index file path on
                //    // NTFS volumes (\\$Extend\\$Quota:$Q:$INDEX_ALLOCATION)
                //    //

                //    Ctx.QuotaIndexFile = true;
                //}
                //else
                //{
                //    FileAttributes attributes = 0;

                //    if (!e.FileName.Contains(":"))
                //    {
                //        attributes = File.GetAttributes(mRootPath + e.FileName);
                //        Ctx.Attributes = (int)attributes;
                //    }

                //    Boolean directoryFile = (attributes & System.IO.FileAttributes.Directory) == System.IO.FileAttributes.Directory;
                //    Boolean reparsePoint = (attributes & System.IO.FileAttributes.ReparsePoint) == System.IO.FileAttributes.ReparsePoint;
                //    Boolean openAsReparsePoint = ((e.Attributes & FILE_FLAG_OPEN_REPARSE_POINT) == FILE_FLAG_OPEN_REPARSE_POINT);

                //    if ((e.NTCreateDisposition == FILE_OPEN) || (e.NTCreateDisposition == FILE_OPEN_IF))
                //    {
                //        if ((directoryFile && !Directory.Exists(mRootPath + e.FileName)) ||
                //            (!directoryFile && !File.Exists(mRootPath + e.FileName)))
                //        {
                //            throw new FileNotFoundException();
                //        }
                //    }

                //    if (reparsePoint && openAsReparsePoint && mCBFSConn.UseReparsePoints)
                //    {
                //        SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                //            GENERIC_READ | FILE_WRITE_ATTRIBUTES,
                //            FileShare.ReadWrite | FileShare.Delete,
                //            IntPtr.Zero,
                //            FileMode.Open,
                //            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                //            IntPtr.Zero
                //            );
                //        if (hFile.IsInvalid)
                //        {
                //            throw new IOException("Could not open file stream.", new Win32Exception(Marshal.GetLastWin32Error()));
                //        }
                //        Ctx.hStream = new FileStream(hFile, FileAccess.ReadWrite);
                //    }
                //    else
                //    if (!directoryFile)
                //    {
                //        if (mEnumerateNamedStreamsEventSet && e.FileName.Contains(":"))
                //        {
                //            FileAccess fileAccess = FileAccess.ReadWrite;
                //            SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                //            GENERIC_READ | GENERIC_WRITE,
                //            FileShare.ReadWrite | FileShare.Delete,
                //            IntPtr.Zero,
                //            FileMode.Open,
                //            FILE_FLAG_BACKUP_SEMANTICS,
                //            IntPtr.Zero
                //            );
                //            if (hFile.IsInvalid && !isWriteRightRequested((CBFileAccess)e.DesiredAccess))
                //            {
                //                fileAccess = FileAccess.Read;
                //                hFile = CreateFile(mRootPath + e.FileName,
                //                  GENERIC_READ,
                //                  FileShare.ReadWrite | FileShare.Delete,
                //                  IntPtr.Zero,
                //                  FileMode.Open,
                //                  FILE_FLAG_BACKUP_SEMANTICS,
                //                  IntPtr.Zero);
                //            }
                //            if (hFile.IsInvalid)
                //            {
                //                throw new IOException("Could not open file stream.", new Win32Exception(Marshal.GetLastWin32Error()));
                //            }

                //            Ctx.hStream = new FileStream(hFile, fileAccess);
                //        }
                //        else
                //        {
                //            try
                //            {
                //                Ctx.hStream = new FileStream(mRootPath + e.FileName, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
                //            }
                //            catch (UnauthorizedAccessException)
                //            {
                //                CBFileAccess acc = (CBFileAccess)e.DesiredAccess;
                //                if (!isReadRightForAttrOrDeleteRequested(acc))
                //                {
                //                    if (isWriteRightForAttrRequested(acc) || !isWriteRightRequested(acc))
                //                        Ctx.hStream = new FileStream(mRootPath + e.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                //                    else
                //                        throw;
                //                }
                //            }
                //            Ctx.FileLength = Ctx.hStream.Length;
                //            if ((e.NTCreateDisposition != FILE_OPEN) && (e.NTCreateDisposition != FILE_OPEN_IF))
                //            {
                //                File.SetAttributes(mRootPath + e.FileName, (FileAttributes)(e.Attributes & 0xFFFF)); // Attributes contains creation flags as well, which we need to strip
                //                Ctx.Attributes = (e.Attributes & 0xFFFF);
                //            }
                //        }
                //    }
                //}
                Ctx.IncrementCounter();
                Ctx.NTCreateDisposition = e.NTCreateDisposition;  // RdS

                #region Submit FILESYSTEM_CREATE_OPEN World Computer event
                FILEOPEN_Operation operation = new FILEOPEN_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), e.FileName, (e.Attributes & (uint)0x10 /*CBFileAttributes.Directory*/) != 0);
                string results = WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                try
                {
                    FILECLOSE_Operation opAttributes = JsonSerializer.Deserialize<FILECLOSE_Operation>(results);
                    //Ctx.CreateTime = opAttributes.CreateTime;
                    //Ctx.LastAccessTime = opAttributes.LastAccessTime;
                    //Ctx.LastWriteTime = opAttributes.LastWriteTime;
                    Ctx.Attributes = opAttributes.Attributes;
                    Ctx.FileLength = opAttributes.FileLength; 
                    Ctx.FileID = opAttributes.FileID;  
                    //Ctx.EventOrigin = opAttributes.EventOrigin;
                }
                catch
                {
                    Debug.Print($"CBOpenFile(ERROR 1) - result={results}");
                    // NOP
                }
                Debug.Print($"CBCreateOpen(A) - FileID={Ctx.FileID} F={e.FileName}, Len={Ctx.FileLength}, IsDir={(Ctx.Attributes & (uint)0x10 /*CBFileAttributes.Directory*/) != 0}");
                #endregion 

                //if (Ctx != null && Ctx.hStream != null)
                //{ 
                //    Debug.Print($"CBOpenFile(B) - fileName={Ctx.hStream.Name} NTCreateDisposition={e.NTCreateDisposition}");
                //}
                //else
                //{
                //    Debug.Print($"CBOpenFile(C) - *** hStream not defined *** -  fileName={e.FileName} NTCreateDisposition={e.NTCreateDisposition}");
                //}
                e.FileContext = GCHandle.ToIntPtr(GCHandle.Alloc(Ctx));
            }
            catch (Exception ex)
            {
                Debug.Print($"CBOpenFile(ERROR2) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBGetFileInfo(object sender, CBFSGetFileInfoEventArgs e)
        {
            try
            {
                var operation = new FILEGETATTRIBUTES_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), e.FileName, 
                        false // NOTE:  placeholder, we don't actually know if it is a directory or not at this point
                        );
                string results = WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                if (results != null && results != "null" /* json empty object */)
                {
                    FILECLOSE_Operation opAttributes = JsonSerializer.Deserialize<FILECLOSE_Operation>(results);
                    e.CreationTime = opAttributes.CreateTime;
                    e.LastAccessTime = opAttributes.LastAccessTime;
                    e.LastWriteTime = opAttributes.LastWriteTime;
                    e.Attributes = opAttributes.Attributes;
                    e.AllocationSize = opAttributes.FileLength;
                    e.HardLinkCount = 1;
                    //e.FileId = 0;
                    e.FileExists = true;
                    e.Size = opAttributes.FileLength;
                }
                else
                {
                    e.FileExists = false;
                }
            }
            catch (Exception ex)
            {
                e.FileExists = false;
                Debug.Print($"CBGetFileInfo(ERROR)  ------------------ {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBEnumerateDirectory(object sender, CBFSEnumerateDirectoryEventArgs e)
        {
            //Debug.Print($"CBEnumerateDirectory(ENTER)");
            DirectoryEnumerationContext context = null;

            try
            {

                if (e.Restart && (e.EnumerationContext != IntPtr.Zero))
                {
                    if (GCHandle.FromIntPtr(e.EnumerationContext).IsAllocated)
                    {
                        GCHandle.FromIntPtr(e.EnumerationContext).Free();
                    }

                    e.EnumerationContext = IntPtr.Zero;
                }
                if (e.EnumerationContext.Equals(IntPtr.Zero))
                {
                    context = new DirectoryEnumerationContext(mRootPath + e.DirectoryName, e.Mask);

                    GCHandle gch = GCHandle.Alloc(context);

                    e.EnumerationContext = GCHandle.ToIntPtr(gch);
                }
                else
                {
                    context = (DirectoryEnumerationContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                }
                e.FileFound = false;

                FileSystemInfo finfo;

                while (e.FileFound = context.GetNextFileInfo(out finfo))
                {
                    if (finfo.Name != "." && finfo.Name != "..") break;
                }
                if (e.FileFound)
                {
                    e.FileName = finfo.Name;

                    e.CreationTime = finfo.CreationTime;
                    e.LastAccessTime = finfo.LastAccessTime;
                    e.LastWriteTime = finfo.LastWriteTime;

                    if ((finfo.Attributes & System.IO.FileAttributes.Directory) != 0)
                    {
                        e.Size = 0;
                    }
                    else
                    {
                        e.Size = ((FileInfo)finfo).Length;
                    }

                    e.AllocationSize = e.Size;

                    e.Attributes = Convert.ToUInt32(finfo.Attributes);

                    e.FileId = 0;

                    //
                    // if reparse points supported then get reparse tag for the file
                    //
                    if ((finfo.Attributes & System.IO.FileAttributes.ReparsePoint) != 0)
                    {

                        if (mCBFSConn.UseReparsePoints)
                        {
                            WIN32_FIND_DATA wfd = new WIN32_FIND_DATA();
                            IntPtr hFile = FindFirstFile(mRootPath + e.DirectoryName + '\\' + e.FileName, out wfd);
                            if (hFile != new IntPtr(-1))
                            {
                                e.ReparseTag = wfd.dwReserved0;
                                FindClose(hFile);
                            }
                            //REPARSE_DATA_BUFFER rdb = new REPARSE_DATA_BUFFER();
                            //IntPtr rdbMem = Marshal.AllocHGlobal(Marshal.SizeOf(rdb));
                            //uint rdbLen = (uint)Marshal.SizeOf(rdb);
                            //CBGetReparseData((CBFSConnect)sender, DirectoryInfo.FileName + '\\' + FileName, rdbMem, ref rdbLen);
                            //Marshal.PtrToStructure(rdbMem, rdb);
                            //Marshal.FreeHGlobal(rdbMem);
                            //ReparseTag = rdb.ReparseTag;
                        }
                    }
                    //
                    // If FileId is supported then get FileId for the file.
                    //
                    if (mGetFileNameByFileIdEventSet)
                    {
                        BY_HANDLE_FILE_INFORMATION fi;
                        SafeFileHandle hFile = CreateFile(mRootPath + e.DirectoryName + '\\' + e.FileName, READ_CONTROL, 0, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

                        if (!hFile.IsInvalid)
                        {
                            if (GetFileInformationByHandle(hFile, out fi) == true)
                                e.FileId = (Int64)((((UInt64)fi.nFileIndexHigh) << 32) | ((UInt64)fi.nFileIndexLow));

                            hFile.Close();
                        }
                        // else
                        // e.ResultCode = Marshal.GetLastWin32Error();
                    }
                }
                else
                {

                    if (GCHandle.FromIntPtr(e.EnumerationContext).IsAllocated)
                    {
                        GCHandle.FromIntPtr(e.EnumerationContext).Free();
                    }

                    e.EnumerationContext = IntPtr.Zero;
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBCloseDirectoryEnumeration(object sender, CBFSCloseDirectoryEnumerationEventArgs e)
        {
            //Debug.Print($"CBCloseDirectoryEnumeration(ENTER)");
            try
            {
                if (e.EnumerationContext != IntPtr.Zero)
                {
                    if (GCHandle.FromIntPtr(e.EnumerationContext).IsAllocated)
                    {
                        GCHandle.FromIntPtr(e.EnumerationContext).Free();
                    }
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBCloseNamedStreamsEnumeration(object sender, CBFSCloseNamedStreamsEnumerationEventArgs e)
        {
            try
            {
                if (e.EnumerationContext != IntPtr.Zero)
                {
                    AlternateDataStreamContext ctx = (AlternateDataStreamContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                    if (!ctx.hFile.Equals(INVALID_HANDLE_VALUE))
                        FindClose(ctx.hFile);
                    GCHandle.FromIntPtr(e.EnumerationContext).Free();
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBSetAllocationSize(object sender, CBFSSetAllocationSizeEventArgs e)
        {
            //NOTHING TO DO;
        }

        private static void CBSetFileSize(object sender, CBFSSetFileSizeEventArgs e)
        {
            try
            {
                //int OffsetHigh = Convert.ToInt32(e.Size >> 32 & 0xFFFFFFFF);

                FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                //FileStream fs = Ctx.hStream;

                //if (fs.SafeFileHandle.IsInvalid)
                //{
                //    e.ResultCode = Marshal.GetLastWin32Error();
                //    return;
                //}

                //fs.Position = e.Size;
                //fs.SetLength(e.Size);
                //fs.Flush();

                Ctx.FileLength = e.Size;
                //If we are in a pending FileCreate operation we need to call into the API to allow it to track the current FileSize
                //if (Ctx.NTCreateDisposition == 2 /*Create*/)
                //{
                //    var operation = new FILESETSIZE_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), Ctx.FileID, e.Size);
                //    WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                //}
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBSetFileAttributes(object sender, CBFSSetFileAttributesEventArgs e)
        {
            Debug.Print($"CBSetFileAttributes(ENTER)");
            try
            {
                string FileName = mRootPath + e.FileName;
                FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                //
                // File class will throw an exception for ADS name
                //
                if (e.FileName.LastIndexOf(':') != -1)
                    FileName = mRootPath + e.FileName.Substring(0, e.FileName.LastIndexOf(':'));
                //Boolean directoryFile = (File.GetAttributes(FileName) & System.IO.FileAttributes.Directory) == System.IO.FileAttributes.Directory;
                //
                // the case when FileAttributes == 0 indicates that file attributes
                // not changed during this callback
                //
                if (e.Attributes != 0)
                {
                    //File.SetAttributes(FileName, (FileAttributes)e.Attributes);
                    Ctx.Attributes = e.Attributes;
                }

                DateTime CreationTime = e.CreationTime;
                if (CreationTime != ZERO_FILETIME)
                {
                    // %TODO% - How to set time for a Directory?  Meta data for Directories??

                    //if (directoryFile)
                    //    Directory.SetCreationTimeUtc(FileName, CreationTime);
                    //else
                    //    File.SetCreationTimeUtc(FileName, CreationTime);
                    Ctx.CreateTime = CreationTime;
                }

                //FileInfo fi = new FileInfo(FileName);
                //bool ro = fi.IsReadOnly;
                //FileAttributes attr = File.GetAttributes(FileName);

                //if (ro)
                //{
                    
                //    File.SetAttributes(FileName, attr & ~FileAttributes.ReadOnly);
                //    Ctx.Attributes = (int)(attr & ~FileAttributes.ReadOnly);
                //}

                DateTime LastAccessTime = e.LastAccessTime;
                if (LastAccessTime != ZERO_FILETIME)
                {
                    // %TODO% - How to set time for a Directory?  Meta data for Directories??

                    //if (directoryFile)
                    //    Directory.SetLastAccessTimeUtc(FileName, LastAccessTime);
                    //else
                    //    File.SetLastAccessTimeUtc(FileName, LastAccessTime);
                    Ctx.LastAccessTime = LastAccessTime;
                }

                DateTime LastWriteTime = e.LastWriteTime;
                if (LastWriteTime != ZERO_FILETIME)
                {
                    // %TODO% - How to set time for a Directory?  Meta data for Directories??
                    //if (directoryFile)
                    //    Directory.SetLastWriteTimeUtc(FileName, LastWriteTime);
                    //else
                    //    File.SetLastWriteTimeUtc(FileName, LastWriteTime);
                    Ctx.LastWriteTime = LastWriteTime;
                }

                //if (ro)
                //{
                //    File.SetAttributes(FileName, attr);
                //    Ctx.Attributes = (int)attr;
                //}
                Ctx.Attributes = e.Attributes;

                //FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                if (Ctx.NTCreateDisposition == 2 /*Create*/)
                {
                    var operation = new FILESETATTRIBUTES_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), e.FileName, Ctx.FileID,
                                                e.CreationTime, e.LastAccessTime, e.LastWriteTime, e.ChangeTime, e.Attributes, e.EventOrigin);
                    WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                }


            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            Debug.Print($"CBSetFileAttributes(LEAVE)");
        }

        private static void CBCanFileBeDeleted(object sender, CBFSCanFileBeDeletedEventArgs e)
        {
            Debug.Print($"CBCanFileBeDeleted(ENTER)");
            e.CanBeDeleted = true;
            bool IsStream = e.FileName.Contains(":");
            if (IsStream)
                return;
            if (((File.GetAttributes(mRootPath + e.FileName) & FileAttributes.Directory) != 0) &&
                ((File.GetAttributes(mRootPath + e.FileName) & FileAttributes.ReparsePoint) == 0))
            {
                var info = new DirectoryInfo(mRootPath + e.FileName);

                if (info.GetFileSystemInfos().Length > 0)
                {
                    e.CanBeDeleted = false;
                    e.ResultCode = ERROR_DIR_NOT_EMPTY;
                }
            }
        }

        private static void CBDeleteFile(object sender, CBFSDeleteFileEventArgs e)
        {
            Debug.Print($"CBDeleteFile(ENTER)");
            try
            {
                //string fullName = mRootPath + e.FileName;
                //bool IsStream = e.FileName.Contains(":");

                //if (mEnumerateNamedStreamsEventSet && IsStream)
                //{
                //    if (!DeleteFile(fullName))
                //    {
                //        if (Marshal.GetLastWin32Error() == ERROR_FILE_NOT_FOUND)
                //            throw new FileNotFoundException(fullName);
                //        else
                //            throw new Exception(fullName);
                //    }
                //}
                //else
                //{
                //    FileSystemInfo info = null;
                //    bool isDir = (File.GetAttributes(fullName) & FileAttributes.Directory) != 0;

                //    if (isDir)
                //    {
                //        info = new DirectoryInfo(fullName);
                //    }
                //    else
                //    {
                //        info = new FileInfo(fullName);
                //    }

                //info.Delete();

                var operation = new FILEDELETE_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), e.FileName,
                    false // NOTE:  Just a placeholder - we don't actually know if this is a file or a directory at this point
                    );
                    WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());
                //}
            }
            catch (Exception ex)
            {
                Debug.Print($"VDrive.CBDeleteFile(ERROR) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            Debug.Print($"...CBDeleteFile(ENTER)");
        }

        private static void CBRenameOrMoveFile(object sender, CBFSRenameOrMoveFileEventArgs e)
        {
            Debug.Print($"CBRenameOrMoveFile(ENTER)");
            try
            {
                //string oldName = mRootPath + e.FileName;
                //string newName = mRootPath + e.NewFileName;
                //if ((File.GetAttributes(oldName) & FileAttributes.Directory) != 0)
                //{
                //    DirectoryInfo dirinfo = new DirectoryInfo(oldName);
                //    dirinfo.MoveTo(newName);

                //}
                //else
                //{
                //    FileInfo finfo = new FileInfo(oldName);

                //    FileInfo finfo1 = new FileInfo(newName);

                //    if (finfo1.Exists)
                //    {
                //        finfo1.Delete();
                //    }
                //    finfo.MoveTo(newName);
                //}
                var operation = new FILERENAMEORMOVE_Operation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), 
                        false,  //Note: placeholder - we don't actuall know if this is a file or a directory at this point 
                        e.FileName, e.NewFileName);
                WCVirtualDiskVolumeMetaDataOperationApi(operation.AsBase64String());

            }
            catch (Exception ex)
            {
                Debug.Print($"VDrive.CBRenameOrMoveFile(ERROR) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            Debug.Print($"...CBRenameOrMoveFile(LEAVE)");
        }

        private static void CBReadFile(object sender, CBFSReadFileEventArgs e)
        {
            //Debug.Print($"CBReadFile(ENTER)");
            try
            {
                FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                //byte[] buff = new byte[e.BytesToRead];
                //Marshal.Copy(e.Buffer, buff, 0, Convert.ToInt32(e.BytesToRead));
                //var bytesRead = mSetManager.ReadSetMemberAsync(Ctx.FileID, Ctx.FileLength, buff, Convert.ToUInt64(e.Position), Convert.ToInt32(e.BytesToRead)).Result;
                var operationRequest = new VolumeDataOperation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), Ctx.FileID, VolumeDataOperationType.FILE_READ,
                                            Convert.ToUInt64(e.Position), Convert.ToUInt32(e.BytesToRead), Convert.ToUInt64(Ctx.FileLength));
                string results = WCVirtualDiskVolumeDataOperationApi(operationRequest.AsBase64String());
                VolumeDataOperation operationResponse = JsonSerializer.Deserialize<VolumeDataOperation>(results);
                //if ( operation.ByteCount != e.BytesToRead )
                //{
                //    Debug.Print($"^^^^^^^^^^^ CBReadFile(ALERT!)  bytesToRead={e.BytesToRead}, actual bytes read={opertaion.ByteCount} ^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
                //}
                Marshal.Copy(operationResponse.ByteBuffer, 0, new IntPtr(e.Buffer.ToInt64()), (int)operationResponse.ByteCount);
                e.BytesRead = operationResponse.ByteCount;
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
            
        }

        private static void CBWriteFile(object sender, CBFSWriteFileEventArgs e)
        {
            
            e.BytesWritten = 0;
            try
            {
                FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                if(Ctx.NTCreateDisposition != 2 /*CREATE*/)
                {
                    e.ResultCode = ERROR_ACCESS_DENIED; // WORM (Write Once Read Many) implemenation can't write to a file in "open" mode.  Can only write to a file that is in "create" mode.
                    return;
                }
                byte[] buff = new byte[e.BytesToWrite];
                Marshal.Copy(e.Buffer, buff, 0, Convert.ToInt32(e.BytesToWrite));
                Ctx.LastWriteTime = DateTime.UtcNow;
                //var bytesWritten = mSetManager.WriteSetMemberAsync(Ctx.FileID, Ctx.FileLength, buff, Convert.ToUInt64(e.Position), Convert.ToInt32(e.BytesToWrite)).Result;
                var operation = new VolumeDataOperation(new Guid(mWorldComputerVirturalDiskID), new Guid(mWorldComputerVirtualDiskVolumeID), Ctx.FileID, VolumeDataOperationType.FILE_WRITE,
                                            Convert.ToUInt64(e.Position), Convert.ToUInt32(e.BytesToWrite), Convert.ToUInt64(Ctx.FileLength), buff);
                string results = WCVirtualDiskVolumeDataOperationApi(operation.AsBase64String());
                VolumeDataOperation opAttributes = JsonSerializer.Deserialize<VolumeDataOperation>(results);
                e.BytesWritten = opAttributes.ByteCount;
                Ctx.FileLength = Convert.ToInt64(opAttributes.FileLength);
            }
            catch (Exception ex)
            {
                Debug.Print($"VDrive.CBWriteFile(ERROR) - {ex}");
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        private static void CBIsDirectoryEmpty(object sender, CBFSIsDirectoryEmptyEventArgs e)
        {
            Debug.Print($"CBIsDirectoryEmpty(ENTER)");
            // Commented out because the used mechanism is very inefficient 
            /*
            DirectoryInfo dir = new DirectoryInfo(mRootPath + e.DirectoryName);
            FileSystemInfo[] fi = dir.GetFileSystemInfos();
            e.IsEmpty = (fi.Length == 0) ? true : false;
            */
        }

        static void CBSetFileSecurity(object sender, CBFSSetFileSecurityEventArgs e)
        {
            Debug.Print($"CBSetFileSecurity(ENTER)");
            //
            // Disable setting of new security for the backend file because
            // these new security attributes can prohibit manipulation
            // with the file from the callbacks (for example if read/write
            // is not allowed for this process).
            //

            if (!SetFileSecurity(mRootPath + e.FileName, (uint)e.SecurityInformation, e.SecurityDescriptor))
            {
                int Error = Marshal.GetLastWin32Error();
                e.ResultCode = Error;
            }
        }

        static void CBGetFileSecurity(object sender, CBFSGetFileSecurityEventArgs e)
        {
            Debug.Print($"CBGetFileSecurity(ENTER)");
            e.DescriptorLength = 0;

            //
            // Getting SACL_SECURITY_INFORMATION requires the program to 
            // execute elevated as well as the SE_SECURITY_NAME privilege
            // to be set. So just remove it from the request.
            //
            uint SACL_SECURITY_INFORMATION = 0x00000008;
            uint SecurityInformation = (uint)e.SecurityInformation;
            SecurityInformation &= ~SACL_SECURITY_INFORMATION;
            if (SecurityInformation == 0)
                return;

            try
            {
                //
                // For recycle bin Windows expects some specific attributes 
                // (if they aren't set then it's reported that the recycle
                // bin is corrupted). So just get attributes from the recycle
                // bin files located on the volume "C:".
                //

                string UpcasePath = e.FileName.ToUpper();
                UInt32 LengthNeeded = 0;
                if (UpcasePath.Contains("$RECYCLE.BIN") ||
                     UpcasePath.Contains("RECYCLER"))
                {
                    string PathOnVolumeC = "C:" + e.FileName;
                    if (!GetFileSecurity(PathOnVolumeC, SecurityInformation, e.SecurityDescriptor, (UInt32)e.BufferLength, ref LengthNeeded))
                    {
                        //
                        // If the SecurityDescriptor buffer is smaller than required then
                        // GetFileSecurity itself sets in the LengthNeeded parameter the required 
                        // size and the last error is set to ERROR_INSUFFICIENT_BUFFER.
                        //
                        int Error = Marshal.GetLastWin32Error();
                        e.ResultCode = Error;
                    }
                }
                else
                {
                    if (!GetFileSecurity(mRootPath + e.FileName, SecurityInformation, e.SecurityDescriptor, (UInt32)e.BufferLength, ref LengthNeeded))
                    {
                        int Error = Marshal.GetLastWin32Error();
                        e.ResultCode = Error;
                    }
                }

                e.DescriptorLength = (int)LengthNeeded;
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }



        #region Not Supported
        private static void CBEnumerateNamedStreams(object sender, CBFSEnumerateNamedStreamsEventArgs e)
        {
            e.NamedStreamFound = false;
            const int bufferSize = 1024;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            AlternateDataStreamContext Ctx = null;

            WIN32_FIND_STREAM_DATA fsd;
            bool searchResult = false;

            fsd.qwStreamSize = 0;
            fsd.cStreamName = "";
            try
            {
                while (!e.NamedStreamFound)
                {
                    if (e.EnumerationContext == IntPtr.Zero)
                    {
                        IntPtr handle = FindFirstStreamW(mRootPath + e.FileName, 0, out fsd, 0);
                        searchResult = !handle.Equals(INVALID_HANDLE_VALUE);
                        Ctx = new AlternateDataStreamContext(handle);
                        if (searchResult)
                            e.EnumerationContext = GCHandle.ToIntPtr(GCHandle.Alloc(Ctx));
                    }
                    else
                    {
                        Ctx = (AlternateDataStreamContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                        searchResult = (Ctx != null && !(Ctx.hFile.Equals(INVALID_HANDLE_VALUE))) && FindNextStreamW(Ctx.hFile, out fsd);
                    }

                    if (!searchResult)
                    {
                        e.ResultCode = Marshal.GetLastWin32Error();
                        if (e.ResultCode == ERROR_HANDLE_EOF)
                            e.ResultCode = 0;
                        else
                        {
                            // when the result code is not ERROR_SUCCESS, OnCloseNamedStreamsEnumeration is not fired
                            if (!(Ctx.hFile.Equals(INVALID_HANDLE_VALUE)))
                                FindClose(Ctx.hFile);
                            Ctx = null;
                            e.EnumerationContext = IntPtr.Zero;
                        }
                        return;
                    }

                    if (fsd.cStreamName == "::$DATA")
                        continue;

                    e.NamedStreamFound = true;

                    e.StreamSize = fsd.qwStreamSize;
                    e.StreamAllocationSize = fsd.qwStreamSize;
                    e.StreamName = fsd.cStreamName;
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
                // when the result code is not ERROR_SUCCESS, OnCloseNamedStreamsEnumeration is not fired
                if (e.ResultCode != 0)
                {
                    if (Ctx != null)
                    {
                        if (!(Ctx.hFile.Equals(INVALID_HANDLE_VALUE)))
                            FindClose(Ctx.hFile);
                        Ctx = null;
                    }
                    e.EnumerationContext = IntPtr.Zero;
                }
            }
        }

        static void CBGetFileNameByFileId(object sender, CBFSGetFileNameByFileIdEventArgs e)
        {
            Debug.Print($"CBGetFileNameByFileId(ENTER)");
            SafeFileHandle rootHandle = null;
            SafeFileHandle fileHandle = null;
            IntPtr fileIdBuffer = IntPtr.Zero;
            IntPtr nameBuffer = IntPtr.Zero;
            IntPtr fileNameInfoBuffer = IntPtr.Zero;

            try
            {
                try
                {
                    rootHandle = CreateFile(mRootPath,
                                             0,
                                             FileShare.ReadWrite | FileShare.Delete,
                                             IntPtr.Zero,
                                             FileMode.Open,
                                             FILE_FLAG_BACKUP_SEMANTICS,
                                             IntPtr.Zero);
                    if (rootHandle.IsInvalid)
                        throw new IOException("Could not open file stream.", new Win32Exception(Marshal.GetLastWin32Error()));


                    fileIdBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(e.FileId));
                    Marshal.WriteInt64(fileIdBuffer, e.FileId);

                    UNICODE_STRING name;
                    name.Length = name.MaximumLength = 8;
                    name.Buffer = fileIdBuffer;

                    nameBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(name));
                    Marshal.StructureToPtr(name, nameBuffer, false);

                    OBJECT_ATTRIBUTES objAttributes = new OBJECT_ATTRIBUTES();

                    objAttributes.Length = (uint)Marshal.SizeOf(objAttributes);
                    objAttributes.ObjectName = nameBuffer;
                    objAttributes.RootDirectory = rootHandle.DangerousGetHandle();
                    objAttributes.Attributes = 0;
                    objAttributes.SecurityDescriptor = IntPtr.Zero;
                    objAttributes.SecurityQualityOfService = IntPtr.Zero;

                    IO_STATUS_BLOCK iosb = new IO_STATUS_BLOCK();

                    uint status = NtOpenFile(out fileHandle,
                                              GENERIC_READ,
                                              ref objAttributes,
                                              ref iosb,
                                              System.IO.FileShare.ReadWrite,
                                              0x00002000 //FILE_OPEN_BY_FILE_ID 
                                              );
                    if (status != 0)
                    {
                        uint win32Error = LsaNtStatusToWinError(status);
                        throw new Win32Exception((int)win32Error);
                    }

                    uint fileNameInfoBufferSize = 512;
                    fileNameInfoBuffer = Marshal.AllocHGlobal((int)fileNameInfoBufferSize);

                    if (!GetFileInformationByHandleEx(fileHandle,
                                                       2, //FileNameInfo
                                                       fileNameInfoBuffer,
                                                       fileNameInfoBufferSize))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    //
                    // Get data from the output buffer
                    //

                    int dataLength = Marshal.ReadInt32(fileNameInfoBuffer);
                    IntPtr stringOffset = new IntPtr(fileNameInfoBuffer.ToInt64() + sizeof(int));
                    e.FilePath = Marshal.PtrToStringUni(stringOffset, dataLength / sizeof(char));
                    //
                    // File path also contains root part of mRootPath (without drive letter).
                    // Remove it.
                    //
                    e.FilePath = e.FilePath.Substring(mRootPath.Length - "X:".Length);
                }
                finally
                {
                    if (rootHandle != null)
                        rootHandle.Close();

                    if (fileHandle != null)
                        fileHandle.Close();

                    if (fileIdBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(fileIdBuffer);

                    if (nameBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(nameBuffer);

                    if (fileNameInfoBuffer != IntPtr.Zero)
                        Marshal.FreeHGlobal(fileNameInfoBuffer);
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBEnumerateHardLinks(object sender, CBFSEnumerateHardLinksEventArgs e)
        {
            HardLinkContext Ctx;

            try
            {
                while (true)
                {
                    if (e.EnumerationContext == IntPtr.Zero)
                    {
                        Ctx = new HardLinkContext();

                        string FullName = mRootPath + e.FileName;
                        uint BufferSize = HardLinkContext.BufferSize;
                        Ctx.FindHandle = FindFirstFileNameW(FullName, 0, ref BufferSize, Ctx.Buffer);
                        if (Ctx.FindHandle.Equals(INVALID_HANDLE_VALUE))
                        {
                            int LastError = Marshal.GetLastWin32Error();
                            Ctx.Dispose();
                            e.ResultCode = LastError;
                            return;
                        }

                        e.LinkName = Marshal.PtrToStringUni(Ctx.Buffer);

                        e.EnumerationContext = GCHandle.ToIntPtr(GCHandle.Alloc(Ctx));
                    }
                    else
                    {
                        Ctx = (HardLinkContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                        uint BufferSize = HardLinkContext.BufferSize;
                        if (!FindNextFileNameW(Ctx.FindHandle, ref BufferSize, Ctx.Buffer))
                        {
                            int LastError = Marshal.GetLastWin32Error();
                            if (LastError == ERROR_HANDLE_EOF)
                            {
                                e.LinkFound = false;
                                return;
                            }
                            else
                            {
                                e.ResultCode = LastError;
                                return;
                            }
                        }

                        e.LinkName = Marshal.PtrToStringUni(Ctx.Buffer);
                    }

                    //
                    // The link name returned from FindFirstFileNameW and FindNextFileNameW can 
                    // contain path without drive letter. 
                    //

                    if (e.LinkName[0] == '\\')
                    {
                        int MountPointLength = 2;

                        for (int i = 2; i < mRootPath.Length; i++, MountPointLength++)
                        {
                            if (mRootPath[i] == '\\')
                                break;
                        }

                        e.LinkName = mRootPath.Remove(MountPointLength, mRootPath.Length - MountPointLength) + e.LinkName;
                    }

                    //
                    // Report only file names (hard link names) that are only
                    // inside the mapped directory. 
                    //
                    if (e.LinkName.StartsWith(mRootPath))
                        break;
                }

                String RootDirectory = Path.GetDirectoryName(e.LinkName);

                e.LinkFound = true;

                //
                // The link name has been obtained. Trim it to the link name without path.
                //

                e.LinkName = Path.GetFileName(e.LinkName);

                //
                // And get file ID for its parent directory. 
                // For the root directory return 0x7FFFFFFFFFFFFFFF, because it's
                // predefined value for the root directory.
                //

                if (RootDirectory.Equals(mRootPath))
                {
                    e.ParentId = 0x7FFFFFFFFFFFFFFF;
                }
                else
                {
                    e.ParentId = 0x7FFFFFFFFFFFFFFF;

                    BY_HANDLE_FILE_INFORMATION fi;
                    SafeFileHandle DirHandle = CreateFile(RootDirectory, READ_CONTROL, 0, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);

                    if (!DirHandle.IsInvalid)
                    {
                        if (GetFileInformationByHandle(DirHandle, out fi))
                        {
                            e.ParentId = fi.nFileIndexHigh;
                            e.ParentId <<= 32;
                            e.ParentId |= fi.nFileIndexLow;
                        }
                        DirHandle.Close();
                    }
                    else
                    {
                        e.ResultCode = Marshal.GetLastWin32Error();
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBCloseHardLinksEnumeration(object sender, CBFSCloseHardLinksEnumerationEventArgs e)
        {
            try
            {
                HardLinkContext Ctx = (HardLinkContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;

                GCHandle.FromIntPtr(e.EnumerationContext).Free();
                Ctx.Dispose();
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBCreateHardLink(object sender, CBFSCreateHardLinkEventArgs e)
        {
            string CurrentName = mRootPath + e.FileName;
            string NewName = mRootPath + e.LinkName;
            if (!CreateHardLinkW(NewName, CurrentName, IntPtr.Zero))
                e.ResultCode = Marshal.GetLastWin32Error();
        }

        static void CBFsctl(object sender, CBFSFsctlEventArgs e)
        {
            e.ResultCode = ERROR_INVALID_FUNCTION;
        }

        static void CBSetReparsePoint(object sender, CBFSSetReparsePointEventArgs e)
        {
            System.Threading.NativeOverlapped Overlapped = new System.Threading.NativeOverlapped();
            FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
            FileStream fs = Ctx.hStream;
            UInt32 BytesReturned = 0;

            try
            {
                if (Ctx.hStream == null)
                {
                    //
                    // there is a directory entry and we didn't get handle in OpenFile callback,
                    // so get it now
                    //
                    SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                        GENERIC_READ | FILE_WRITE_ATTRIBUTES,
                        FileShare.ReadWrite | FileShare.Delete,
                        IntPtr.Zero,
                        FileMode.Open,
                        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                        IntPtr.Zero
                        );
                    fs = new FileStream(hFile, System.IO.FileAccess.ReadWrite);
                    Ctx.hStream = fs;
                }

                if (fs.SafeFileHandle.IsInvalid)
                {
                    e.ResultCode = Marshal.GetLastWin32Error();
                    return;
                }

                if (!DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), // handle to file or directory
                    FSCTL_SET_REPARSE_POINT, // dwIoControlCode
                    e.ReparseBuffer,         // input buffer 
                    (UInt32)e.ReparseBufferLength, // size of input buffer
                    IntPtr.Zero,             // lpOutBuffer
                    0,                       // nOutBufferSize
                    ref BytesReturned,       // lpBytesReturned
                    ref Overlapped))         // OVERLAPPED structure
                {
                    e.ResultCode = Marshal.GetLastWin32Error();
                    return;
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBGetReparsePoint(object sender, CBFSGetReparsePointEventArgs e)
        {

            try
            {
                System.Threading.NativeOverlapped Overlapped = new System.Threading.NativeOverlapped();

                bool fsLocal = false;

                FileStreamContext Ctx = null;
                FileStream fs = null;

                if (e.FileContext != IntPtr.Zero)
                {

                    Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
                    fs = Ctx.hStream;
                }
                else
                    fsLocal = true;

                UInt32 BytesReturned = 0;
                bool Result;

                try
                {
                    if (fs == null)
                    {
                        SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                            GENERIC_READ | FILE_WRITE_ATTRIBUTES,
                            FileShare.ReadWrite | FileShare.Delete,
                            IntPtr.Zero,
                            FileMode.Open,
                            FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                            IntPtr.Zero
                            );
                        fs = new FileStream(hFile, System.IO.FileAccess.ReadWrite);
                        if (!fsLocal)
                            Ctx.hStream = fs;
                    }

                    if (fs.SafeFileHandle.IsInvalid)
                    {
                        e.ResultCode = Marshal.GetLastWin32Error();
                        return;
                    }

                    Result = DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), // handle to file or directory
                        FSCTL_GET_REPARSE_POINT, // dwIoControlCode
                        IntPtr.Zero,             // input buffer 
                        0,                       // size of input buffer
                        e.ReparseBuffer,         // lpOutBuffer
                        (UInt32)e.ReparseBufferLength, // nOutBufferSize
                        ref BytesReturned,       // lpBytesReturned
                        ref Overlapped);         // OVERLAPPED structure

                    e.ReparseBufferLength = (int)BytesReturned;
                    if (!Result)
                    {
                        e.ResultCode = Marshal.GetLastWin32Error();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    e.ResultCode = ExceptionToErrorCode(ex);
                }
                finally
                {
                    if (fsLocal && fs != null)
                    {
                        fs.Close();
                        fs.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBDeleteReparsePoint(object sender, CBFSDeleteReparsePointEventArgs e)
        {
            System.Threading.NativeOverlapped Overlapped = new System.Threading.NativeOverlapped();
            FileStreamContext Ctx = (FileStreamContext)GCHandle.FromIntPtr(e.FileContext).Target;
            FileStream fs = Ctx.hStream;
            UInt32 BytesReturned = 0;

            try
            {
                if (Ctx.hStream == null)
                {
                    //
                    // there is a directory entry and we didn't get handle in OpenFile callback,
                    // so get it now
                    //
                    SafeFileHandle hFile = CreateFile(mRootPath + e.FileName,
                        GENERIC_READ | FILE_WRITE_ATTRIBUTES,
                        FileShare.ReadWrite | FileShare.Delete,
                        IntPtr.Zero,
                        FileMode.Open,
                        FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                        IntPtr.Zero
                        );
                    fs = new FileStream(hFile, System.IO.FileAccess.ReadWrite);
                    Ctx.hStream = fs;
                }

                if (fs.SafeFileHandle.IsInvalid)
                {
                    e.ResultCode = Marshal.GetLastWin32Error();
                    return;
                }

                if (!DeviceIoControl(fs.SafeFileHandle.DangerousGetHandle(), // handle to file or directory
                    FSCTL_DELETE_REPARSE_POINT, // dwIoControlCode
                    e.ReparseBuffer,            // input buffer 
                    (UInt32)e.ReparseBufferLength, // size of input buffer
                    IntPtr.Zero,                // lpOutBuffer
                    0,                          // nOutBufferSize
                    ref BytesReturned,          // lpBytesReturned
                    ref Overlapped))            // OVERLAPPED structure
                {
                    e.ResultCode = Marshal.GetLastWin32Error();
                    return;
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

#if DISK_QUOTAS_SUPPORTED

        static void CBQueryQuotas(object sender, CBFSQueryQuotasEventArgs e)
        {
            try
            {
                DiskQuotasContext ctx = null;

                if (e.EnumerationContext == null)
                {
                    ctx = new DiskQuotasContext(mRootPath);
                    e.EnumerationContext = GCHandle.ToIntPtr(GCHandle.Alloc(ctx));
                }
                else
                { // Context is not null
                    ctx = (DiskQuotasContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                }

                if (e.SID != null)
                {
                    e.QuotaFound = ctx.FindUserBySid(e.SID, e.SIDLength);
                }
                else
                {
                    e.QuotaFound = ctx.FindUserByIndex((uint)e.Index);
                }

                if (e.QuotaFound)
                {
                    e.QuotaLimit = (Int64)ctx.UserQuotaLimit;
                    e.QuotaThreshold = (Int64)ctx.UserQuotaThreshold;
                    e.QuotaUsed = (Int64)ctx.UserQuotaUsed;
                    if ((e.SID == null) && (e.SIDOut != null) && (ctx.UserSid.BinaryLength <= e.SIDOutLength))
                    {
                        byte[] SidBuf = new byte[ctx.UserSid.BinaryLength];
                        ctx.UserSid.GetBinaryForm(SidBuf, 0);
                        Marshal.Copy(SidBuf, 0, e.SIDOut, SidBuf.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBSetQuotas(object sender, CBFSSetQuotasEventArgs e)
        {
            try
            {
                DiskQuotasContext ctx = null;

                if (e.EnumerationContext == null)
                {

                    ctx = new DiskQuotasContext(mRootPath);
                    e.EnumerationContext = GCHandle.ToIntPtr(GCHandle.Alloc(ctx));
                }
                else
                { // Context is not null

                    ctx = (DiskQuotasContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                }

                if (ctx.FindUserBySid(e.SID, e.SIDLength))
                {
                    if (e.RemoveQuota)
                        ctx.DeleteUser();
                    else
                    {
                        ctx.UserQuotaLimit = e.QuotaLimit;
                        ctx.UserQuotaThreshold = e.QuotaThreshold;
                    }
                    e.QuotaFound = true;
                }
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        static void CBCloseQuotasEnumeration(object sender, CBFSCloseQuotasEnumerationEventArgs e)
        {
            try
            {
                DiskQuotasContext Ctx = (DiskQuotasContext)GCHandle.FromIntPtr(e.EnumerationContext).Target;
                GCHandle.FromIntPtr(e.EnumerationContext).Free();
                Ctx.Dispose();
            }
            catch (Exception ex)
            {
                e.ResultCode = ExceptionToErrorCode(ex);
            }
        }

        /*
        static void CBSetQuotasControlInformation(object sender, CBFSSetQuotasControlInformationEventArgs e)
        {
          uint Status;
          SafeFileHandle hFile = null;
          FILE_FS_CONTROL_INFORMATION ffci;
          UNICODE_STRING name;
          uint LastError;
          IntPtr Buffer = IntPtr.Zero;
          IntPtr NameBuffer = IntPtr.Zero;

          try
          {
            string quotaFile = "\\??\\" + mRootPath[0] + ":\\$Extend\\$Quota:$Q:$INDEX_ALLOCATION";
            Buffer = Marshal.StringToHGlobalUni(quotaFile);
            name.Length = name.MaximumLength = (ushort)(quotaFile.Length * sizeof(char));
            name.Buffer = Buffer;

            NameBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(name));
            Marshal.StructureToPtr(name, NameBuffer, false);

            OBJECT_ATTRIBUTES objAttributes = new OBJECT_ATTRIBUTES();

            objAttributes.Length = (uint)Marshal.SizeOf(objAttributes);
            objAttributes.ObjectName = NameBuffer;
            objAttributes.RootDirectory = IntPtr.Zero;
            objAttributes.Attributes = 0;
            objAttributes.SecurityDescriptor = IntPtr.Zero;
            objAttributes.SecurityQualityOfService = IntPtr.Zero;

            IO_STATUS_BLOCK iosb = new IO_STATUS_BLOCK();

            LastError = LsaNtStatusToWinError(NtOpenFile(out hFile,
                FILE_READ_DATA | FILE_WRITE_DATA,
                ref objAttributes,
                ref iosb,
                FileShare.ReadWrite,
                0x00000001 //FILE_OPEN
                ));

            if (LastError != 0)
              throw new CBFSConnectCustomException((int)LastError);

            ffci = new FILE_FS_CONTROL_INFORMATION();
            ffci.DefaultQuotaLimit = e.DefaultQuotaInfoLimit;
            ffci.DefaultQuotaThreshold = e.DefaultQuotaInfoThreshold;
            ffci.FileSystemControlFlags = (uint)e.FileSystemControlFlags;

            Status = NtSetVolumeInformationFile(hFile,
                ref iosb,
                ref ffci,
                Marshal.SizeOf(ffci),
                FSINFOCLASS.FileFsControlInformation);

            LastError = LsaNtStatusToWinError(Status);
            if (LastError != 0)
              throw new CBFSConnectCustomException((int)LastError);
          }
          finally
          {
            if (hFile != null) hFile.Close();
            if (Buffer != IntPtr.Zero) Marshal.FreeHGlobal(Buffer);
            if (NameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(NameBuffer);
          }
        }

        static void CBQueryQuotasControlInformation(object sender, CBFSQueryQuotasControlInformationEventArgs e)
        {
          uint Status;
          SafeFileHandle hFile = null;
          FILE_FS_CONTROL_INFORMATION ffci;
          UNICODE_STRING name;
          uint LastError;
          IntPtr Buffer = IntPtr.Zero;
          IntPtr NameBuffer = IntPtr.Zero;

          try
          {
            string quotaFile = "\\??\\" + mRootPath[0] + ":\\$Extend\\$Quota:$Q:$INDEX_ALLOCATION";
            Buffer = Marshal.StringToHGlobalUni(quotaFile);
            name.Length = name.MaximumLength = (ushort)(quotaFile.Length * sizeof(char));
            name.Buffer = Buffer;

            NameBuffer = Marshal.AllocHGlobal(Marshal.SizeOf(name));
            Marshal.StructureToPtr(name, NameBuffer, false);

            OBJECT_ATTRIBUTES objAttributes = new OBJECT_ATTRIBUTES();

            objAttributes.Length = (uint)Marshal.SizeOf(objAttributes);
            objAttributes.ObjectName = NameBuffer;
            objAttributes.RootDirectory = IntPtr.Zero;
            objAttributes.Attributes = 0;
            objAttributes.SecurityDescriptor = IntPtr.Zero;
            objAttributes.SecurityQualityOfService = IntPtr.Zero;

            IO_STATUS_BLOCK iosb = new IO_STATUS_BLOCK();

            Status = NtOpenFile(out hFile,
                FILE_READ_DATA | FILE_WRITE_DATA,
                ref objAttributes,
                ref iosb,
                FileShare.ReadWrite,
                0x00000001 //FILE_OPEN
                );

            LastError = LsaNtStatusToWinError(Status);

            if (LastError != 0)
              throw new CBFSConnectCustomException((int)LastError);

            ffci = new FILE_FS_CONTROL_INFORMATION();

            Status = NtQueryVolumeInformationFile(hFile,
                ref iosb,
                ref ffci,
                Marshal.SizeOf(ffci),
                FSINFOCLASS.FileFsControlInformation);

            LastError = LsaNtStatusToWinError(Status);

            if (LastError != 0)
              throw new CBFSConnectCustomException((int)LastError);

            e.DefaultQuotaInfoThreshold = ffci.DefaultQuotaThreshold;
            e.DefaultQuotaInfoLimit = ffci.DefaultQuotaLimit;
            e.FileSystemControlFlags = ffci.FileSystemControlFlags;
          }
          finally
          {
            if (hFile != null) hFile.Close();
            if (Buffer != IntPtr.Zero) Marshal.FreeHGlobal(Buffer);
            if (NameBuffer != IntPtr.Zero) Marshal.FreeHGlobal(NameBuffer);
          }
        }
    */

#endif //DISK_QUOTAS_SUPPORTED

        static void CBEjectStorage(object Sender, CBFSEjectedEventArgs e)
        {
            Debug.Print($"CBEjectStorage(ENTER)");
        }

        static Int64 GetFileId(string Path)
        {
            Debug.Print($"GetFileId(ENTER)");
            Int64 FileId = 0;
            BY_HANDLE_FILE_INFORMATION fi;
            SafeFileHandle FileHandle = CreateFile(Path, READ_CONTROL, 0, IntPtr.Zero, FileMode.Open, FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);

            if (!FileHandle.IsInvalid)
            {
                if (GetFileInformationByHandle(FileHandle, out fi))
                {
                    FileId = fi.nFileIndexHigh;
                    FileId <<= 32;
                    FileId |= fi.nFileIndexLow;
                }
                FileHandle.Close();
            }
            return FileId;
        }

        #endregion 
        #endregion




        #region PInvoke Declarations
        static private IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        private const int ERROR_INVALID_FUNCTION = 1;
        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PATH_NOT_FOUND = 3;
        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_INVALID_DRIVE = 15;
        private const int ERROR_HANDLE_EOF = 38;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;
        private const int ERROR_OPERATION_ABORTED = 0x3e3;
        private const int ERROR_INTERNAL_ERROR = 0x54f;
        private const int ERROR_PRIVILEGE_NOT_HELD = 1314;
        private const int ERROR_DIRECTORY = 0x10b;
        private const int ERROR_FILENAME_TOO_LONG = 0xCE;
        private const int ERROR_DIR_NOT_EMPTY = 0x91;

        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        private const uint FILE_READ_DATA = 0x0001;
        private const uint FILE_WRITE_DATA = 0x0002;
        private const uint FILE_APPEND_DATA = 0x0004;

        private const uint FILE_READ_EA = 0x0008;
        private const uint FILE_WRITE_EA = 0x0010;

        private const uint FILE_READ_ATTRIBUTES = 0x0080;
        private const uint FILE_WRITE_ATTRIBUTES = 0x0100;
        private const uint FILE_ATTRIBUTE_ARCHIVE = 0x00000020;

        private const uint SYNCHRONIZE = 0x00100000;

        private const uint READ_CONTROL = 0x00020000;
        private const uint STANDARD_RIGHTS_READ = READ_CONTROL;
        private const uint STANDARD_RIGHTS_WRITE = READ_CONTROL;

        private const uint GENERIC_READ = STANDARD_RIGHTS_READ |
                                            FILE_READ_DATA |
                                            FILE_READ_ATTRIBUTES |
                                            FILE_READ_EA |
                                            SYNCHRONIZE;

        private const uint GENERIC_WRITE = STANDARD_RIGHTS_WRITE |
                                            FILE_WRITE_DATA |
                                            FILE_WRITE_ATTRIBUTES |
                                            FILE_WRITE_EA |
                                            FILE_APPEND_DATA |
                                            SYNCHRONIZE;

        private const uint FSCTL_SET_REPARSE_POINT = 0x900A4;
        private const uint FSCTL_GET_REPARSE_POINT = 0x900A8;
        private const uint FSCTL_DELETE_REPARSE_POINT = 0x900AC;

        private const int SECTOR_SIZE = 512;

        private const uint FILE_OPEN = 0x00000001;
        private const uint FILE_OPEN_IF = 0x00000003;

        private static readonly DateTime ZERO_FILETIME = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public enum StreamType
        {
            Data = 1,
            ExternalData = 2,
            SecurityData = 3,
            AlternateData = 4,
            Link = 5,
            PropertyData = 6,
            ObjectID = 7,
            ReparseData = 8,
            SparseDock = 9
        }

        public enum FSINFOCLASS
        {
            FileFsVolumeInformation = 1,
            FileFsLabelInformation,      // 2
            FileFsSizeInformation,       // 3
            FileFsDeviceInformation,     // 4
            FileFsAttributeInformation,  // 5
            FileFsControlInformation,    // 6
            FileFsFullSizeInformation,   // 7
            FileFsObjectIdInformation,   // 8
            FileFsDriverPathInformation, // 9
            FileFsMaximumInformation
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct FILE_FS_CONTROL_INFORMATION
        {
            public Int64 FreeSpaceStartFiltering;
            public Int64 FreeSpaceThreshold;
            public Int64 FreeSpaceStopFiltering;
            public Int64 DefaultQuotaThreshold;
            public Int64 DefaultQuotaLimit;
            public uint FileSystemControlFlags;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public struct Win32StreamID
        {
            public StreamType dwStreamId;
            public int dwStreamAttributes;
            public long Size;
            public int dwStreamNameSize;
            // WCHAR cStreamName[1]; 
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct REPARSE_DATA_BUFFER
        {
            public uint ReparseTag;
            public ushort ReparseDataLength;
            public ushort Reserved;
            public ushort SubstituteNameOffset;
            public ushort SubstituteNameLength;
            public ushort PrintNameOffset;
            public ushort PrintNameLength;
            // uncomment if full length reparse point must be read
            //[MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x3FF0)]
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0xF)]
            public byte[] PathBuffer;
        }

        public class FileStreamContext
        {
            public FileStreamContext()
            {
                OpenCount = 0;
                QuotaIndexFile = false;
                hStream = null!;
                NTCreateDisposition = 0;  // RdS
                CreateTime = DateTime.Now;
                LastAccessTime = DateTime.MinValue;
                LastWriteTime = DateTime.MinValue;
                ChangeTime = DateTime.MinValue;
                IsDirty = false;

            }
            public void IncrementCounter()
            {
                ++OpenCount;
            }
            public void DecrementCounter()
            {
                --OpenCount;
            }
            public bool QuotaIndexFile;
            public int OpenCount;
            public FileStream hStream;


            public int NTCreateDisposition; // RdS
            public Guid FileID;             // Rds
            public int RefCount = 1;        // Rds
            public DateTime CreateTime;     // Rds
            public DateTime LastAccessTime; // Rds
            public DateTime LastWriteTime;  // Rds
            public DateTime ChangeTime;     // Rds
            public int Attributes;          // Rds
            public int EventOrigin;         // Rds
            public long FileLength;         // Rds
            public bool IsDirty;            // RdS

            static public void SetLastWriteTimeUtc(FileStreamContext Ctx, DateTime datetimeutc)
            {
                Ctx.LastWriteTime = datetimeutc;
                Ctx.IsDirty = true;
            }
        }

        public class AlternateDataStreamContext
        {
            public AlternateDataStreamContext(IntPtr Value)
            {
                hFile = Value;
                Context = IntPtr.Zero;
            }
            public IntPtr hFile;
            public IntPtr Context;
        }

        public class HardLinkContext : IDisposable
        {
            public IntPtr FindHandle;
            public static uint BufferSize = 512;
            public IntPtr Buffer;

            private bool disposed = false;

            public HardLinkContext()
            {
                FindHandle = IntPtr.Zero;
                Buffer = Marshal.AllocHGlobal((int)BufferSize * sizeof(Char));
            }

            ~HardLinkContext()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                // Check to see if Dispose has already been called. 
                if (this.disposed)
                    return;

                if (disposing)
                {
                    // Dispose managed resources.
                    ;
                }

                if (FindHandle != IntPtr.Zero)
                {

                    FindClose(FindHandle);
                    FindHandle = IntPtr.Zero;
                }

                Marshal.FreeHGlobal(Buffer);

                disposed = true;
            }
        }
        [DllImport("ole32.dll")]
        static extern int OleInitialize(IntPtr pvReserved);

#if DISK_QUOTAS_SUPPORTED
        public class DiskQuotasContext : IDisposable
        {

            private DiskQuotaControlClass controlDiskQuotas;
            private DIDiskQuotaUser currentUser = null;
            private bool disposed = false;

            public DiskQuotasContext(string Path)
            {
                controlDiskQuotas = new DiskQuotaControlClass();
                controlDiskQuotas.Initialize(Path[0] + ":\\", true);
                controlDiskQuotas.UserNameResolution = UserNameResolutionConstants.dqResolveSync;
            }

            public bool FindUserBySid(IntPtr SidPtr, int SidLength)
            {
                if (SidPtr == null)
                    return false;

                byte[] SidBuf = new byte[SidLength];
                Marshal.Copy(SidPtr, SidBuf, 0, SidLength);
                SecurityIdentifier Sid = new SecurityIdentifier(SidBuf, 0);
                return FindUserBySid(Sid);
            }

            public bool FindUserBySid(SecurityIdentifier Sid)
            {
                currentUser = controlDiskQuotas.FindUser(Sid.Value);
                if (null == currentUser) return false;
                return true;
            }

            public bool FindUserByIndex(UInt32 Index)
            {
                bool result = false;

                System.Collections.IEnumerator e = controlDiskQuotas.GetEnumerator();
                e.Reset();
                try
                {
                    while (e.MoveNext())
                    {
                        if (Index-- == 0)
                        {
                            currentUser = (DIDiskQuotaUser)e.Current;
                            result = true;
                            break;
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    result = false;
                }
                return result;
            }

            public void DeleteUser()
            {
                if (currentUser != null)
                {
                    controlDiskQuotas.DeleteUser(currentUser);
                    currentUser = null;
                }
            }

            public double UserQuotaLimit
            {
                get
                {
                    if (currentUser != null) return currentUser.QuotaLimit;
                    return controlDiskQuotas.QuotaLimit;
                }
                set
                {
                    if (currentUser != null) currentUser.QuotaLimit = value;
                    else
                        controlDiskQuotas.QuotaLimit = value;
                }
            }

            public double UserQuotaUsed
            {
                get
                {
                    if (currentUser != null)
                        return currentUser.QuotaUsed;
                    else
                        return controlDiskQuotas.QuotaUsed;
                }
            }

            public double UserQuotaThreshold
            {
                get
                {
                    if (currentUser != null) return currentUser.QuotaThreshold;
                    return controlDiskQuotas.QuotaThreshold;
                }
                set
                {
                    if (currentUser != null)
                        currentUser.QuotaThreshold = value;
                    else
                        controlDiskQuotas.QuotaThreshold = value;
                }
            }

            public SecurityIdentifier UserSid
            {
                get
                {
                    if (currentUser != null)
                    {
                        return new SecurityIdentifier(controlDiskQuotas.TranslateLogonNameToSID(currentUser.LogonName));
                    }
                    else
                        throw new InvalidOperationException();
                }
            }

            public string GetUserNameFromSid(SecurityIdentifier Sid)
            {
                string userAccountName;
                try
                {
                    NTAccount acc = (NTAccount)Sid.Translate(typeof(NTAccount));
                    userAccountName = acc.Value;
                }
                catch (System.Exception)
                {
                    userAccountName = string.Empty;
                }
                return userAccountName;
            }

            ~DiskQuotasContext()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
                GC.SuppressFinalize(this);
            }

            protected virtual void Dispose(bool disposing)
            {
                // Check to see if Dispose has already been called. 
                if (this.disposed)
                    return;

                if (disposing)
                {
                    // Dispose managed resources.
                    ;
                }
                disposed = true;
            }
        }
#endif //DISK_QUOTAS_SUPPORTED

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct BY_HANDLE_FILE_INFORMATION
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint dwVolumeSerialNumber;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint nNumberOfLinks;
            public uint nFileIndexHigh;
            public uint nFileIndexLow;
        };

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern Boolean BackupRead(SafeFileHandle hFile,
            IntPtr lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            [MarshalAs(UnmanagedType.Bool)] Boolean bAbort,
            [MarshalAs(UnmanagedType.Bool)] Boolean bProcessSecurity,
            ref IntPtr lpContext
            );

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool BackupSeek(SafeFileHandle hFile,
            uint dwLowBytesToSeek,
            uint dwHighBytesToSeek,
            out uint lpdwLowByteSeeked,
            out uint lpdwHighByteSeeked,
            ref IntPtr lpContext
            );

        // The CharSet must match the CharSet of the corresponding PInvoke signature
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct WIN32_FIND_DATA
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_STREAM_DATA
        {
            public long qwStreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string cStreamName;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, [In] ref System.Threading.NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, IntPtr lpBuffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, [In] ref System.Threading.NativeOverlapped lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string lpFileName,
            uint dwDesiredAccess, FileShare dwShareMode,
            IntPtr lpSecurityAttributes, FileMode dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION lpFileInformation);

        [DllImport("Advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileSecurity(string FileName, uint SecurityInformation, IntPtr SecurityDecriptor);

        [DllImport("Advapi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileSecurity(string FileName,
            uint SecurityInformation, IntPtr SecurityDecriptor, uint Length, ref uint LengthNeeded);

        [DllImport("advapi32.dll")]
        private static extern UInt32 GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileNameW(string FileName,
            uint dwFlags,
            ref uint StringLength,
            IntPtr LinkName
            );

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextFileNameW(IntPtr hFindStream,
            ref uint StringLength,
            IntPtr LinkName
            );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstStreamW(string FileName, uint dwLevel, out WIN32_FIND_STREAM_DATA lpFindStreamData, uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextStreamW(IntPtr hFindStream, out WIN32_FIND_STREAM_DATA lpFindStreamData);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateHardLinkW(String FileName, String ExistingFileName, IntPtr lpSecurityAttributes);

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct OBJECT_ATTRIBUTES
        {
            internal UInt32 Length;
            internal IntPtr RootDirectory;
            internal IntPtr ObjectName;
            internal uint Attributes;
            internal IntPtr SecurityDescriptor;
            internal IntPtr SecurityQualityOfService;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct UNICODE_STRING
        {
            internal ushort Length;
            internal ushort MaximumLength;
            internal IntPtr Buffer;
        };

        [StructLayout(LayoutKind.Sequential, Pack = 0)]
        private struct IO_STATUS_BLOCK
        {
            internal uint Status;
            internal IntPtr information;
        }

        [DllImport("ntdll.dll", ExactSpelling = true, SetLastError = false)]
        private static extern uint NtOpenFile(
            out Microsoft.Win32.SafeHandles.SafeFileHandle FileHandle,
            uint DesiredAccess,
            ref OBJECT_ATTRIBUTES ObjectAttributes,
            ref IO_STATUS_BLOCK IoStatusBlock,
            System.IO.FileShare ShareAccess,
            uint OpenOptions
            );

        [DllImport("Advapi32.dll", ExactSpelling = true, SetLastError = false)]
        private static extern uint LsaNtStatusToWinError(uint Status);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle FileHandle,
            uint FileInformationClass,
            IntPtr FileInformation,
            uint BufferSize
            );

        [DllImport("ntdll.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern uint NtSetVolumeInformationFile(
            Microsoft.Win32.SafeHandles.SafeFileHandle FileHandle,
            ref IO_STATUS_BLOCK IoStatusBlock,
            ref FILE_FS_CONTROL_INFORMATION FsInformation,
            int Length,
            FSINFOCLASS FsInformationClass
            );

        [DllImport("ntdll.dll", CharSet = CharSet.Auto, SetLastError = false)]
        private static extern uint NtQueryVolumeInformationFile(
            Microsoft.Win32.SafeHandles.SafeFileHandle FileHandle,
            ref IO_STATUS_BLOCK IoStatusBlock,
            ref FILE_FS_CONTROL_INFORMATION FsInformation,
            int Length,
            FSINFOCLASS FsInformationClass
            );

        [DllImport("Kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool DeviceIoControl(
            IntPtr hDevice,
            uint dwIoControlCode,
            IntPtr InBuffer,
            UInt32 nInBufferSize,
            IntPtr OutBuffer,
            UInt32 nOutBufferSize,
            ref UInt32 pBytesReturned,
            [In] ref System.Threading.NativeOverlapped lpOverlapped);

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern bool GetDiskFreeSpaceEx(
            string lpDirectoryName,
            out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes,
            out ulong lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool DeleteFile(string lpFileName);

        const string QUOTA_INDEX_FILE = "\\$Extend\\$Quota:$Q:$INDEX_ALLOCATION";

        //
        //Input:
        //  Path - source file path relative to CBFS root path
        //Output:
        // returns TRUE - if Path is a part of QUOTA_INDEX_FILE path
        // FileId set to NTFS disk quotas index file if Path equal to QUOTA_INDEX_FILE,
        // otherwise FileId = 0
        //
        static bool IsQuotasIndexFile(string Path, ref Int64 FileId)
        {
            FileId = 0;
            if (Path.Length != 1 && Path[1] == '$')
            {

                if (QUOTA_INDEX_FILE.StartsWith(Path))
                {

                    if (Path.Equals(QUOTA_INDEX_FILE))
                    {

                        FileId = GetFileId(mRootPath[0] + ":" + Path);
                    }
                    return true;
                }
            }
            return false;
        }

        [Flags]
        public enum CBFileAttributes : uint
        {
            Readonly = 1,
            Hidden = 2,
            System = 4,
            Directory = 0x10,
            Archive = 0x20,
            Device = 0x40,
            Normal = 0x80,
            Temporary = 0x100,
            SparseFile = 0x200,
            ReparsePoint = 0x400,
            Compressed = 0x800,
            Offline = 0x1000,
            NotContentIndexed = 0x2000,
            Encrypted = 0x4000,
            OpenNoRecall = 0x100000,
            OpenReparsePoint = 0x200000,
            FirstPipeInstance = 0x80000,
            PosixSemantics = 0x1000000,
            BackupSemantics = 0x2000000,
            DeleteOnClose = 0x4000000,
            SequentialScan = 0x8000000,
            RandomAccess = 0x10000000,
            NoBuffering = 0x20000000,
            Overlapped = 0x40000000,
            Write_Through = 0x80000000
        }

        [Flags]
        public enum CBFileAccess : uint
        {
            AccessSystemSecurity = 0x1000000,
            Delete = 0x10000,
            file_read_data = 1,
            file_list_directory = 1,
            file_add_file = 2,
            file_write_data = 2,
            file_add_subdirectory = 4,
            file_append_data = 4,
            file_create_pipe_instance = 4,
            file_read_ea = 8,
            file_write_ea = 0x10,
            file_traverse = 0x20,
            file_execute = 0x20,
            file_delete_child = 0x40,
            file_read_attributes = 0x80,
            file_write_attributes = 0x100,
            file_all_access = 0x1f01ff,
            file_generic_execute = 0x1200a0,
            file_generic_read = 0x120089,
            file_generic_write = 0x120116,
            GenericAll = 0x10000000,
            GenericExecute = 0x20000000,
            GenericRead = 0x80000000,
            GenericWrite = 0x40000000,
            MaximumAllowed = 0x2000000,
            ReadControl = 0x20000,
            specific_rightts_all = 0xffff,
            SpecificRightsAll = 0xffff,
            StandardRightsAll = 0x1f0000,
            StandardRightsExecute = 0x20000,
            StandardRightsRead = 0x20000,
            StandardRightsRequired = 0xf0000,
            StandardRightsWrite = 0x20000,
            Synchronize = 0x100000,
            WriteDAC = 0x40000,
            WriteOwner = 0x80000
        }

        public enum CBDriverState
        {
            SERVICE_STOPPED = 1,
            SERVICE_START_PENDING = 2,
            SERVICE_STOP_PENDING = 3,
            SERVICE_RUNNING = 4,
            SERVICE_CONTINUE_PENDING = 5,
            SERVICE_PAUSE_PENDING = 6,
            SERVICE_PAUSED = 7
        }


        #endregion
    }

    #region Dictory Enumeration Context
    public class DirectoryEnumerationContext
    {
        private DirectoryEnumerationContext()
        {

        }
        public bool GetNextFileInfo(out FileSystemInfo info)
        {
            bool Result = false;
            info = null;
            if (mIndex < mFileList.Length)
            {
                info = mFileList[mIndex];
                ++mIndex;
                info.Refresh();
                Result = info.Exists;
            }
            return Result;
        }

        public DirectoryEnumerationContext(string DirName, string Mask)
        {
            DirectoryInfo dirinfo = new DirectoryInfo(DirName);

            mFileList = dirinfo.GetFileSystemInfos(Mask);

            mIndex = 0;
        }
        private FileSystemInfo[] mFileList;
        private int mIndex;
    }
    #endregion 

 
}
