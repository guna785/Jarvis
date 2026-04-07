using Android.Content;
using Jarvis.Contract;
using System;
using System.Collections.Generic;
using System.Text;

namespace Jarvis.Services
{
    public class ContinuousMicImplementation : IContinuousMicService
    {
        public void StartListening()
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(Jarvis.Services.AndroidContinuousMicService));

            if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
                context.StartForegroundService(intent);
            else
                context.StartService(intent);
        }

        public void StopListening()
        {
            var context = global::Android.App.Application.Context;
            var intent = new Intent(context, typeof(Jarvis.Services.AndroidContinuousMicService));
            context.StopService(intent);
        }
    }
}
