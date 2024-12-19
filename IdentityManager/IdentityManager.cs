namespace UnoSysKernel
{
    using Microsoft.Extensions.Logging;
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using UnoSys.Api.Exceptions;
    using UnoSysCore;
    

    internal sealed class IdentityManager : SecuredKernelService, IIdentityManager
    {
        #region Member Fields
        private ITime timeManager = null!;
        private ILocalNodeContext localNodeContext = null!;
        private IGlobalPropertiesContext _globalPropertiesContext = null!;
        private IWorldComputerContext worldComputerContext = null!;
        private IGeneralLedgerManager glManager = null!;
        private IWorldComputerCryptologyContext worldComputerCryptologyContext = null!;
        private IVirtualDiskManager virtualDiskManager = null!;
        private IDefaultGlobalVirtualDriveContext defaultGlobalVirtualDriveContext = null!;
        #endregion

        #region Constructors
        public IdentityManager( IGlobalPropertiesContext gProps, 
                                ILoggerFactory loggerFactory, 
                                IKernelConcurrencyManager concurrencyManager, 
                                ITime time,
                                IKernelContext kcontext, 
                                IIdentityContext identityContext, 
                                IWorldComputerContext worldcomputercontext, 
                                IVirtualDiskManager virtualdiskmanager,
                                IGeneralLedgerManager generalLedgerManager, 
                                IWorldComputerCryptologyContext worldcomputercryptologycontext, 
                                IDefaultGlobalVirtualDriveContext defaultVDriveContext,
                                IWorldComputerCryptologyContext wcCryptologyContext,    
                                ILocalNodeContext localnodecontext, 
                                ISecurityContext securityContext) : base(loggerFactory.CreateLogger("IdentityManager"), concurrencyManager)
        {
            _globalPropertiesContext = gProps;
            localNodeContext = localnodecontext;
            worldComputerContext = worldcomputercontext;
            glManager = generalLedgerManager;
            worldComputerCryptologyContext = worldcomputercryptologycontext;
            virtualDiskManager = virtualdiskmanager!;
            defaultGlobalVirtualDriveContext = defaultVDriveContext;
            // Create a secured object for this kernel service 
            securedObject = securityContext.CreateDefaultKernelServiceSecuredObject();
        }
        #endregion

        #region IDisposable Implementation
        public override void Dispose()
        {
            glManager = null!;
            worldComputerContext = null!;
            localNodeContext = null!;
            _globalPropertiesContext = null!;
            virtualDiskManager = null!;
            timeManager = null!;
            worldComputerCryptologyContext = null!;
            defaultGlobalVirtualDriveContext = null!;
            base.Dispose();
        }
        #endregion

        #region IdentityManager Implementation
        
        public async Task UserCreateAsync(UserSessionToken userSessionToken, string userName, string password)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion

            #region Generate required User-related Blob content
            (Guid userID, string? userDID, Guid userDIDRef ) userIDs = worldComputerContext!.GenerateUserIDs();
            IJurisdictionMember userMember = await glManager.CreateJurisdictionMemeberGeneralLedgerAsync(userIDs.userDIDRef.ToString("N"), userName, null!).ConfigureAwait(false);
            var userCredentialsID = HostCryptology.ComputeUserHashFromUserNameAndPassword(userName, password);
            var userNameID = HostCryptology.ComputeUserHashFromUserName(userName);
            (Guid userConnectionDIDRef, byte[] encryptedUserConnection, byte[] encryptedUserMember) userUserState =
                    await worldComputerContext!.CreateUserAsync(userMember, userIDs.userID, userIDs.userDID, userIDs.userDIDRef, 
                                                    userSessionToken, userCredentialsID,  userName).ConfigureAwait(false);
            #endregion

            #region Create all required Blobs
            Task<bool>[] blobCreateTasks = new Task<bool>[3];
            blobCreateTasks[0] = BlobStorage.CreateBlobAsync(userCredentialsID, userUserState.userConnectionDIDRef.ToByteArray());
            blobCreateTasks[1] = BlobStorage.CreateBlobAsync(userUserState.userConnectionDIDRef, userUserState.encryptedUserConnection);
            blobCreateTasks[2] = BlobStorage.CreateBlobAsync(userNameID, userUserState.encryptedUserMember);
            Task<bool[]> ioSucceededResults = Task.WhenAll<bool>(blobCreateTasks);
            try
            {
                ioSucceededResults.Wait();
            }
            catch (AggregateException)
            {
                throw new UnoSysUnexpectedException("UserCreate(1) failed to complete.");
            }
            if (ioSucceededResults.Status == TaskStatus.RanToCompletion)
            {
                if (!ioSucceededResults.Result[0])
                {
                    if (ioSucceededResults.Result[1])
                    {
                        // Delete the userConnection blob that was successfully created since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userUserState.userConnectionDIDRef).ConfigureAwait(false);
                    }
                    if (ioSucceededResults.Result[2])
                    {
                        // Delete the userNameMember blob that was successfully created since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userNameID).ConfigureAwait(false);
                    }
                    throw new UnoSysResourceAlreadyDefinedException("userCredentials already defined.");
                }
                if (!ioSucceededResults.Result[1])
                {
                    if (ioSucceededResults.Result[0])
                    {
                        // Delete the userCredentialsID blob that was successfully created (otherwise can't get here) since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userCredentialsID).ConfigureAwait(false);
                    }
                    if (ioSucceededResults.Result[2])
                    {
                        // Delete the userNameMember blob that was successfully created since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userNameID).ConfigureAwait(false);
                    }
                    throw new UnoSysResourceAlreadyDefinedException("userConnection already defined.");
                }
                if (!ioSucceededResults.Result[2])
                {
                    if (ioSucceededResults.Result[0])
                    {
                        // Delete the userCredentialsID blob that was successfully created (otherwise can't get here) since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userCredentialsID).ConfigureAwait(false);
                    }
                    if (ioSucceededResults.Result[1])
                    {
                        // Delete the userConnection blob that was successfully created since its either all 3 or none at all semantics
                        await BlobStorage.DeleteBlobAsync(userUserState.userConnectionDIDRef).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                throw new UnoSysUnexpectedException("UserCreate(2) failed to complete.");
            }
            #endregion 
        }


        public async Task UserDeleteAsync(UserSessionToken userSessionToken, string userName, string password /*Guid encryptedUserCredentialsDidRef*/)
        {

            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion
            var userCredentialsID = HostCryptology.ComputeUserHashFromUserNameAndPassword(userName, password);
            var userNameID = HostCryptology.ComputeUserHashFromUserName(userName);
            var userConnectionDID = await BlobStorage.ReadBlobAsync(userCredentialsID).ConfigureAwait(false);
            if (userConnectionDID == null)
            {
                throw new UnoSysResourceNotFoundException("user not found.");
            }
            Guid userConnectionDIDRef = new Guid(userConnectionDID);
            // %TODO% - write linearly instead of in parallel for more control in error conditions %%%%%%%%%%%%%%%%%%%%%%%%%%%%
            Task<bool>[] blobDeleteTasks = new Task<bool>[3];
            blobDeleteTasks[0] = BlobStorage.DeleteBlobAsync(userCredentialsID);
            blobDeleteTasks[1] = BlobStorage.DeleteBlobAsync(userConnectionDIDRef);
            blobDeleteTasks[2] = BlobStorage.DeleteBlobAsync(userNameID);
            Task<bool[]> ioSucceededResults = Task.WhenAll<bool>(blobDeleteTasks);
            try
            {
                ioSucceededResults.Wait();
            }
            catch (AggregateException)
            {
                throw new UnoSysUnexpectedException("UserDelete(1) failed to complete.");
            }

            if (ioSucceededResults.Status == TaskStatus.RanToCompletion)
            {
                if (!ioSucceededResults.Result[0])
                {
                    throw new UnoSysUnexpectedException("Failed to Delete userCredentials blob.");  // Unexpected since just read it above
                }

                if (!ioSucceededResults.Result[1])
                {
                    // NOP - best effort in deleting the userConnection - as long as userCredentials blob has been deleted can't get to userConnection
                    //       so can ignore any errors here
                }

                if (!ioSucceededResults.Result[2])
                {
                    // NOP - best effort in deleting the userNameID 
                }
            }
            else
            {
                throw new UnoSysUnexpectedException("UserDelete(2) failed to complete.");
            }

            //// %TODO% - change to work like Login in that rather than take an encryptedUserCrendtialsDidRef -
            //// have it recompute the userCrentialsDidRef from passed in userName and password and then Delete the blob


            //// %TODO% - Check the user requesting to delete a User  has the correct permissions
            ////var userDIDRef = await worldComputerContext!.DeleteUserAsync(userSessionToken, encryptedUserCredentialsDidRef).ConfigureAwait(false);
            //#region Decrypt userCredentialsDidRefBytes
            //var userCredentialsDidRefBytes = encryptedUserCredentialsDidRef.ToByteArray();
            //HostCryptology.DecryptBufferInPlaceWith32ByteKey(userCredentialsDidRefBytes, worldComputerCryptologyContext!.WorldComputerOsUserSymmKey); // Inplace Encrypted with WorldComputer "OsUser" symmetric key
            //#endregion

            //if (!await defaultVirtualDrive!.DeleteBlobAsync(new Guid(userCredentialsDidRefBytes)).ConfigureAwait(false))
            //{
            //    throw new UnoSysInvalidOperationException("Failed to Delete UserConnection blob.");
            //}
        }


        public async Task<UserSessionToken> UserLoginAsync( /*UserSessionToken contextUserSessionToken,*/ string userName, string password)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(contextUserSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion
            var userCredentialsID = HostCryptology.ComputeUserHashFromUserNameAndPassword(userName, password);
            Guid userConnectionDIDRef = new Guid(await BlobStorage.ReadBlobAsync(userCredentialsID).ConfigureAwait(false));
            byte[] encryptedUserConnection = await BlobStorage.ReadBlobAsync(userConnectionDIDRef).ConfigureAwait(false);
            if (encryptedUserConnection == null!)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            byte[] encryptedUserMember = await BlobStorage.ReadBlobAsync(HostCryptology.ComputeUserHashFromUserName(userName)).ConfigureAwait(false);
            if (encryptedUserMember == null)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            var userSessionToken = new UserSessionToken();
            worldComputerContext!.RegisterUserConnection(userSessionToken, encryptedUserConnection, encryptedUserMember, userCredentialsID);

            #region Destroy sensitive cryptographic material
            for (int i = 0; i < encryptedUserConnection.Length; i++)
            {
                encryptedUserConnection[i] = 0;
            }
            encryptedUserConnection = null!;
            userCredentialsID = Guid.Empty;
            #endregion
            return userSessionToken;
        }


        public async Task UserLogoutAsync(UserSessionToken userSessionToken)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion

            try
            {
                worldComputerContext!.UnregisterUserConnection(userSessionToken);
                await Task.CompletedTask;
            }
            catch (Exception)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
        }


        public async Task UserChangePasswordAsync(UserSessionToken userSessionToken, string userName, string oldPassword, string newPassword)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion
            // Determine and read old userConnectionDIDRef
            var userOldCredentialsDidRef = HostCryptology.ComputeUserHashFromUserNameAndPassword(userName, oldPassword);
            Guid userOldConnectionDIDRef = new Guid(await BlobStorage.ReadBlobAsync(userOldCredentialsDidRef).ConfigureAwait(false));
            byte[] encryptedUserConnection = await BlobStorage.ReadBlobAsync(userOldConnectionDIDRef).ConfigureAwait(false);
            if (encryptedUserConnection == null!)
            {
                throw new UnoSysUnauthorizedAccessException();
            }
            var userNameID = HostCryptology.ComputeUserHashFromUserName(userName);
            byte[] encryptedUserMember = await BlobStorage.ReadBlobAsync(userNameID).ConfigureAwait(false);
            if (encryptedUserMember == null!)
            {
                throw new UnoSysUnauthorizedAccessException();
            }

            var userNewCredentialsID = HostCryptology.ComputeUserHashFromUserNameAndPassword(userName, newPassword);

            encryptedUserConnection = worldComputerContext!.UpdateUserConnectionCredentials(encryptedUserConnection, userNewCredentialsID);
            await UserDeleteAsync(userSessionToken, userName, oldPassword).ConfigureAwait(false);
            Task<bool>[] blobCreateTasks = new Task<bool>[3];
            blobCreateTasks[0] = BlobStorage.CreateBlobAsync(userNewCredentialsID, userOldConnectionDIDRef.ToByteArray());
            blobCreateTasks[1] = BlobStorage.CreateBlobAsync(userOldConnectionDIDRef, encryptedUserConnection);
            blobCreateTasks[2] = BlobStorage.CreateBlobAsync(userNameID, encryptedUserMember);
            Task<bool[]> ioSucceededResults = Task.WhenAll<bool>(blobCreateTasks);
            try
            {
                ioSucceededResults.Wait();
            }
            catch (AggregateException)
            {
                throw new UnoSysUnexpectedException("UserUpdatePassword(1) failed to complete.");
            }

            if (ioSucceededResults.Status == TaskStatus.RanToCompletion)
            {
                if (!ioSucceededResults.Result[0])
                {
                    throw new UnoSysInvalidOperationException("Failed to Create userCredentials blob.");
                }

                if (!ioSucceededResults.Result[1])
                {
                    throw new UnoSysInvalidOperationException("Failed to Create userConnection blob.");
                }

                if (!ioSucceededResults.Result[2])
                {
                    throw new UnoSysInvalidOperationException("Failed to Create userMember blob.");
                }
            }
            else
            {
                throw new UnoSysUnexpectedException("UserUpdatePassword(2) failed to complete.");
            }

            #region Destroy sensitive cryptographic material
            for (int i = 0; i < encryptedUserConnection.Length; i++)
            {
                encryptedUserConnection[i] = 0;
            }
            encryptedUserConnection = null!;
            #endregion
        }


        public async Task<string> UserCreateApplicationAsync(UserSessionToken userSessionToken, ISessionToken subjectSessionToken, string applicationName,
                                       string applicationDescription, Guid appKey)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion
            
            (Guid appConnectionDIDRef, byte[] encryptedApplicationConnection) userApplicationState =
                    await worldComputerContext!.CreateApplicationAsync(subjectSessionToken, applicationName, applicationDescription, appKey).ConfigureAwait(false);

            if (!await BlobStorage.CreateBlobAsync(userApplicationState.appConnectionDIDRef, userApplicationState.encryptedApplicationConnection).ConfigureAwait(false))
            {
                throw new UnoSysInvalidOperationException("Failed to create ApplicationConnection blob.");
            }

            var encryptedAppDidRefBytes = userApplicationState.appConnectionDIDRef.ToByteArray();
            HostCryptology.EncryptBufferInPlaceWith32ByteKey(encryptedAppDidRefBytes, worldComputerCryptologyContext!.WorldComputerOsAppSymmKey); // Inplace Encrypted with WorldComputer "OsApp" symmetric key

            return new Guid(encryptedAppDidRefBytes).ToString("N").ToUpper();  // returns the generated encryptedAddDidRef as the applicationId
        }


        public async Task UserDeleteApplicationAsync(UserSessionToken userSessionToken, ISessionToken subjectSessionToken, Guid encryptedAppDidRef)
        {
            #region Validate SessionToken
            //if (!worldComputerContext!.IsValidSessionToken(userSessionToken))
            //{
            //    throw new UnoSysUnauthorizedAccessException();
            //}
            #endregion
            // %TODO% - Check the user requesting to delete an Application  has the correct permissions
            var appDIDRef = await worldComputerContext!.DeleteApplicationAsync(subjectSessionToken, encryptedAppDidRef).ConfigureAwait(false);
            if (!await BlobStorage.DeleteBlobAsync(appDIDRef).ConfigureAwait(false))
            {
                throw new UnoSysInvalidOperationException("Failed to Delete ApplicationConnectoin blob.");
            }
        }

        //public IGeneralLedger ResolveUserGL(UserSessionToken userSessionToken)
        //{
        //    return worldComputerContext!.ResolveUserGL(userSessionToken);
        //}

        #endregion

        #region IKernelService Implementation
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
            {
                await base.StartAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            using (var exclusiveLock = await _concurrencyManager.KernelLock.WriterLockAsync().ConfigureAwait(false))
            {
                await base.StopAsync(cancellationToken);
            }
        }


        #endregion

        #region Helpers
        private IBlobStorage BlobStorage
        {
            get
            {
                IBlobStorage blobStorage = null!;
                #region Redirect to Local InMemory Virtual Disk if World Computer virtual disk not yet initialized
                if (worldComputerContext!.WorldComputerVirtualDisk == null)
                {
                    blobStorage = (IBlobStorage?)defaultGlobalVirtualDriveContext!.BootVirtualDrive!;
                }
                else
                {
                    virtualDiskManager!.EnsureIsWorldComputerVirtualDiskMountedAsync().Wait();
                    blobStorage = worldComputerContext!.WorldComputerVirtualDriveBlobStorage!;
                }
                #endregion
                return blobStorage;
            }
        }
        #endregion
    }
}
