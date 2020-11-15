using Ryujinx.Common.Logging;
using Ryujinx.HLE.HOS.Services.Arp;
using System;

namespace Ryujinx.HLE.HOS.Services.Pctl.ParentalControlServiceFactory
{
    class IParentalControlService : IpcService
    {
        private int   _permissionFlag;
        private ulong _titleId;
        private bool  _freeCommunicationEnabled;
        private int[] _ratingAge;
        private bool  _featuresRestriction                 = false;
        private bool  _stereoVisionRestrictionConfigurable = true;
        private bool  _stereoVisionRestriction             = false;

        public IParentalControlService(ServiceCtx context, bool withInitialize, int permissionFlag)
        {
            _permissionFlag = permissionFlag;

            if (withInitialize)
            {
                Initialize(context);
            }
        }

        [Command(1)] // 4.0.0+
        // Initialize()
        public ResultCode Initialize(ServiceCtx context)
        {
            if ((_permissionFlag & 0x8001) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            ResultCode resultCode = ResultCode.InvalidPid;

            if (context.Process.Pid != 0)
            {
                if ((_permissionFlag & 0x40) == 0)
                {
                    ulong titleId = ApplicationLaunchProperty.GetByPid(context).TitleId;

                    if (titleId != 0)
                    {
                        _titleId = titleId;

                        // TODO: Call nn::arp::GetApplicationControlProperty here when implemented, if it return ResultCode.Success we assign fields.
                        _ratingAge                = Array.ConvertAll(context.Device.Application.ControlData.Value.RatingAge.ToArray(), Convert.ToInt32);
                        _freeCommunicationEnabled = context.Device.Application.ControlData.Value.ParentalControl == LibHac.Ns.ParentalControlFlagValue.FreeCommunication;
                    }
                }

                if (_titleId != 0)
                {
                    // TODO: Service store some private fields in another static object.

                    if ((_permissionFlag & 0x8040) == 0)
                    {
                        // TODO: Service store TitleId and FreeCommunicationEnabled in another static object.
                        //       When it's done it signal an event in this static object.
                        Logger.Stub?.PrintStub(LogClass.ServicePctl);
                    }
                }

                resultCode = ResultCode.Success;
            }

            return resultCode;
        }

        [Command(1001)]
        // CheckFreeCommunicationPermission()
        public ResultCode CheckFreeCommunicationPermission(ServiceCtx context)
        {
            Logger.Stub?.PrintStub(LogClass.ServicePctl);

            if (!_freeCommunicationEnabled)
            {
                return ResultCode.FreeCommunicationDisabled;
            }

            return ResultCode.Success;
        }

        [Command(1013)] // 4.0.0+
        // ConfirmStereoVisionPermission()
        public ResultCode ConfirmStereoVisionPermission(ServiceCtx context)
        {
            return IsStereoVisionPermittedImpl();
        }

        [Command(1061)] // 4.0.0+
        // ConfirmStereoVisionRestrictionConfigurable()
        public ResultCode ConfirmStereoVisionRestrictionConfigurable(ServiceCtx context)
        {
            if ((_permissionFlag & 2) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            if (_stereoVisionRestrictionConfigurable)
            {
                return ResultCode.Success;
            }
            else
            {
                return ResultCode.StereoVisionRestrictionConfigurableDisabled;
            }
        }

        [Command(1062)] // 4.0.0+
        // GetStereoVisionRestriction() -> bool
        public ResultCode GetStereoVisionRestriction(ServiceCtx context)
        {
            if ((_permissionFlag & 0x200) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            bool stereoVisionRestriction = false;

            if (_stereoVisionRestrictionConfigurable)
            {
                stereoVisionRestriction = _stereoVisionRestriction;
            }

            context.ResponseData.Write(stereoVisionRestriction);

            return ResultCode.Success;
        }

        [Command(1063)] // 4.0.0+
        // SetStereoVisionRestriction(bool)
        public ResultCode SetStereoVisionRestriction(ServiceCtx context)
        {
            if ((_permissionFlag & 0x200) == 0)
            {
                return ResultCode.PermissionDenied;
            }

            bool stereoVisionRestriction = context.RequestData.ReadBoolean();

            if (!_featuresRestriction)
            {
                if (_stereoVisionRestrictionConfigurable)
                {
                    _stereoVisionRestriction = stereoVisionRestriction;

                    // TODO: It signals an internal event of service. We have to determine where this event is used. 
                }
            }

            return ResultCode.Success;
        }

        [Command(1064)] // 5.0.0+
        // ResetConfirmedStereoVisionPermission()
        public ResultCode ResetConfirmedStereoVisionPermission(ServiceCtx context)
        {
            return ResultCode.Success;
        }

        [Command(1065)] // 5.0.0+
        // IsStereoVisionPermitted() -> bool
        public ResultCode IsStereoVisionPermitted(ServiceCtx context)
        {
            bool isStereoVisionPermitted = false;

            ResultCode resultCode = IsStereoVisionPermittedImpl();

            if (resultCode == ResultCode.Success)
            {
                isStereoVisionPermitted = true;
            }

            context.ResponseData.Write(isStereoVisionPermitted);

            return resultCode;
        }

        private ResultCode IsStereoVisionPermittedImpl()
        {
            /*
                // TODO: Application Exemptions are readed from file "appExemptions.dat" in the service savedata.
                //       Since we don't support the pctl savedata for now, this can be implemented later.

                if (appExemption)
                {
                    return ResultCode.Success;
                }
            */

            if (_stereoVisionRestrictionConfigurable && _stereoVisionRestriction)
            {
                return ResultCode.StereoVisionDenied;
            }
            else
            {
                return ResultCode.Success;
            }
        }
    }
}