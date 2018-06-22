﻿using System;
using System.Threading;
using System.Collections.Generic;
using System.Windows;

using Thermo.Interfaces.ExactiveAccess_V1;
using Thermo.Interfaces.InstrumentAccess_V1.MsScanContainer;
using IMsScan = Thermo.Interfaces.InstrumentAccess_V2.MsScanContainer.IMsScan;

namespace RealTimeGUI
{
	/// <summary>
	/// Show incoming data packets and signals of acquisition start, acquisition stop and each scan.
	/// </summary>
	public class DataReceiver
	{
		internal DataReceiver() { }

        public IExactiveInstrumentAccess InstrumentAccess { get; set; }

        public IMsScanContainer ScanContainer { get; set; }

        public List<IMsScan> ListScan { get; set; }
        
        public RTParameters RTParameters { get; set; }

        public static event EventHandler<NotificationEventArgs> DataReceiverNotificationEventHandler;

        internal void ReceiveData()
        {
            string x = "Start receive scans on detector " + ScanContainer.DetectorClass + ".";
            DataReceiverNotificationEventHandler?.Invoke(this, new NotificationEventArgs(x + "\n"));

            ScanContainer.AcquisitionStreamOpening += Orbitrap_AcquisitionStreamOpening;
            ScanContainer.AcquisitionStreamClosing += Orbitrap_AcquisitionStreamClosing;
            ScanContainer.MsScanArrived += Orbitrap_MsScanArrived;
        }

        internal void StopReceiveData()
        {
            string x = "Stop receive scans on detector " + ScanContainer.DetectorClass + "...";
            DataReceiverNotificationEventHandler?.Invoke(this, new NotificationEventArgs(x + "\n"));
            ScanContainer.MsScanArrived -= Orbitrap_MsScanArrived;
            ScanContainer.AcquisitionStreamClosing -= Orbitrap_AcquisitionStreamClosing;
            ScanContainer.AcquisitionStreamOpening -= Orbitrap_AcquisitionStreamOpening;
        }

        private void Orbitrap_MsScanArrived(object sender, MsScanEventArgs e)
		{
			// If examining code takes longer, in particular for some scans, it is wise
			// to use a processing queue in order to get the system as responsive as possible.

			using (IMsScan scan = (IMsScan) e.GetScan())	// caution! You must dispose this, or you block shared memory!
			{
                //Console.WriteLine("\n{0:HH:mm:ss,fff} scan with {1} centroids arrived", DateTime.Now, scan.CentroidCount);
                DataReceiverNotificationEventHandler?.Invoke(this, new NotificationEventArgs("..."));
            }
		}

		private void Orbitrap_AcquisitionStreamClosing(object sender, EventArgs e)
		{
            string x = "\n{0:HH:mm:ss,fff} {1}" + DateTime.Now + "Acquisition stream closed (end of method)" + "\n";
            DataReceiverNotificationEventHandler?.Invoke(this, new NotificationEventArgs(x));
        }

		private void Orbitrap_AcquisitionStreamOpening(object sender, MsAcquisitionOpeningEventArgs e)
		{
            string x = "\n{0:HH:mm:ss,fff} {1}" + DateTime.Now + "Acquisition stream opens (start of method)" + "\n";
            DataReceiverNotificationEventHandler?.Invoke(this, new NotificationEventArgs(x));
        }
	}
}
