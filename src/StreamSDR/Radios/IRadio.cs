/*
 * This file is part of StreamSDR.
 *
 * StreamSDR is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * StreamSDR is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with StreamSDR. If not, see <https://www.gnu.org/licenses/>.
 */

using System;

namespace StreamSDR.Radios
{
    /// <summary>
    /// Provides a generic interface to control and receive samples from SDR radios.
    /// </summary>
    internal interface IRadio : IDisposable
    {
        /// <summary>
        /// The device name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The sample rate the device is operating at in Hertz.
        /// </summary>
        public uint SampleRate { get; set; }

        /// <summary>
        /// The centre frequency the device is tuned to in Hertz.
        /// </summary>
        public ulong Frequency { get; set; }

        /// <summary>
        /// Event fired when samples have been received from the device, provided as an array of bytes containing interleaved IQ samples.
        /// </summary>
        public event EventHandler<byte[]>? SamplesAvailable;

        /// <summary>
        /// Starts the device.
        /// </summary>
        public void Start();

        /// <summary>
        /// Stops the device.
        /// </summary>
        public void Stop();
    }
}