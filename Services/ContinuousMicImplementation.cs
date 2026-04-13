using Android.Content;
using Visor.Contract;
using System;
using System.Collections.Generic;
using System.Text;

namespace Visor.Services
{
    public class ContinuousMicImplementation : IContinuousMicService
    {
        public void StartListening()
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(Visor.Services.AndroidContinuousMicService));
            intent.SetAction("START_LISTENING");

            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }

        public void StopListening()
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(Visor.Services.AndroidContinuousMicService));
            intent.SetAction("STOP_LISTENING");

            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }
    }
}
