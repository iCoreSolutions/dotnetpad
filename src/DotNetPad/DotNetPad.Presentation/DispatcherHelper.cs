﻿using System.Security.Permissions;
using System.Windows.Threading;

namespace Waf.DotNetPad.Presentation
{
    public static class DispatcherHelper
    {
        /// <summary>
        /// Execute the event queue of the dispatcher.
        /// </summary>
        [SecurityPermissionAttribute(SecurityAction.Demand, Flags = SecurityPermissionFlag.UnmanagedCode)]
        public static void DoEvents()
        {
            DispatcherFrame frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
                new DispatcherOperationCallback(ExitFrame), frame);
            Dispatcher.PushFrame(frame);
        }

        private static object ExitFrame(object frame)
        {
            ((DispatcherFrame)frame).Continue = false;
            return null;
        }
    }
}
