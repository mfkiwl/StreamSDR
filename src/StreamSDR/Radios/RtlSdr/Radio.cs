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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StreamSDR.Radios.RtlSdr
{
    /// <summary>
    /// Provides access to control and receive samples from a rtl-sdr radio.
    /// </summary>
    internal class Radio : IRadio
    {
        #region Private fields
        /// <summary>
        /// <see langword="true"/> if Dispose() has been called, <see langword="false"/> otherwise.
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The application lifetime service.
        /// </summary>
        private readonly IHostApplicationLifetime _applicationLifetime;

        /// <summary>
        /// The device handle.
        /// </summary>
        private IntPtr _device;

        /// <summary>
        /// The worker thread used to receive samples from the radio.
        /// </summary>
        private readonly Thread _receiverThread;

        /// <summary>
        /// The callback used to process the samples that have been read.
        /// </summary>
        private readonly Interop.ReadDelegate _readCallback;

        /// <summary>
        /// The mode in which the radio's gain is operating.
        /// </summary>
        private GainMode _gainMode = GainMode.Automatic;
        #endregion

        #region Properties
        /// <inheritdoc/>
        public string Name { get; private set; } = string.Empty;

        /// <inheritdoc/>
        public uint SampleRate
        {
            get => _device != IntPtr.Zero ? Interop.GetSampleRate(_device) : 0;
            set
            {
                if (Interop.SetSampleRate(_device, value) == 0)
                {
                    _logger.LogInformation($"Setting the sample rate to {value.ToString("N0", Thread.CurrentThread.CurrentCulture)} Hz");
                }
                else
                {
                    _logger.LogError($"Unable to set the sample rate to {value.ToString("N0", Thread.CurrentThread.CurrentCulture)} Hz");
                }
            }
        }

        /// <inheritdoc/>
        public ulong Frequency
        {
            get => _device != IntPtr.Zero ? Interop.GetCenterFreq(_device) : 0;
            set
            {
                NumberFormatInfo numberFormat = new NumberFormatInfo();
                numberFormat.NumberGroupSeparator = ".";

                if (Interop.SetCenterFreq(_device, (uint)value) == 0)
                {
                    _logger.LogInformation($"Setting the frequency to {value.ToString("N0", numberFormat)} Hz");
                }
                else
                {
                    _logger.LogError($"Unable to set the centre frequency to {value.ToString("N0", numberFormat)} Hz");
                }
            }
        }

        /// <inheritdoc/>
        public float Gain
        {
            get => _device != IntPtr.Zero ? Interop.GetTunerGain(_device) / 10f : 0f;
            set
            {
                int gain = (int)MathF.Floor(value * 10);

                if (Interop.SetTunerGain(_device, gain) == 0)
                {
                    _logger.LogInformation($"Setting the gain to {value} dB");
                }
                else
                {
                    _logger.LogError($"Unable to set the gain to {value} dB");
                }
            }
        }

        /// <inheritdoc/>
        public GainMode GainMode
        {
            get => _gainMode;
            set
            {
                int gainMode = value == GainMode.Manual ? 1 : 0;

                if (Interop.SetTunerGainMode(_device, gainMode) == 0)
                {
                    _gainMode = value;
                    _logger.LogInformation($"Setting the gain mode to {value}");
                }
                else
                {
                    _logger.LogError($"Unable to set the gain mode to {value}");
                }
            }
        }

        /// <inheritdoc/>
        public float[] GainLevelsSupported
        {
            get
            {
                // Get the number of gains supported by the tuner
                int numberOfGains = Interop.GetTunerGains(_device, null);

                // If the number of gains is 0 or negative, return an error
                if (numberOfGains < 1)
                {
                    _logger.LogError($"Unable to get the levels of gain supported by the tuner");
                    return Array.Empty<float>();
                }

                // Get the supported gains
                int[] gains = new int[numberOfGains];
                Interop.GetTunerGains(_device, gains);

                // Convert to floats and return
                return Array.ConvertAll(gains, item => item / 10f);
            }
        }
        #endregion

        #region Events
        /// <inheritdoc/>
        public event EventHandler<byte[]>? SamplesAvailable;
        #endregion

        #region Constructor, finaliser and lifecycle methods
        /// <summary>
        /// Initialises a new instance of the <see cref="RtlSdrRadio"/> class.
        /// </summary>
        public unsafe Radio(ILogger<Radio> logger, IHostApplicationLifetime lifetime)
        {
            // Store a reference to the logger
            _logger = logger;

            // Store a reference to the application lifetime
            _applicationLifetime = lifetime;

            // Create the sample receiver worker thread
            _receiverThread = new(ReceiverWorker)
            {
                Name = "ReceiverThread"
            };

            // Set the sample reading callback
            _readCallback = new Interop.ReadDelegate(ProcessSamples);
        }

        /// <summary>
        /// Finalises the instance of the <see cref="RtlSdrRadio"/> class.
        /// </summary>
        ~Radio() => Dispose();

        /// <inheritdoc/>
        public void Dispose()
        {
            // Return if already disposed
            if (_disposed)
            {
                return;
            }

            // Stop the device if it is running
            Stop();

            // Set that dispose has run
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        /// <inheritdoc/>
        public void Start()
        {
            // Log that the radio is starting
            _logger.LogInformation("Starting the rtl-sdr radio");

            try
            {
                // Check if a rtl-sdr device is available
                if (Interop.GetDeviceCount() < 1)
                {
                    _logger.LogCritical("No rtl-sdr devices could be found");
                    _applicationLifetime.StopApplication();
                    return;
                }

                // Get the device name
                Name = Interop.GetDeviceName(0);

                // Open the device
                if (Interop.Open(out _device, 0) != 0)
                {
                    _logger.LogCritical("The rtl-sdr device could not be opened");
                    _applicationLifetime.StopApplication();
                    return;
                }

                // Set the initial state
                Frequency = 100000000;
                SampleRate = 2048000;

                // Start the receiver thread
                _receiverThread.Start();

                // Log that the radio has started
                _logger.LogInformation($"Started the radio: {Name}");
            }
            catch (DllNotFoundException)
            {
                _logger.LogCritical("Unable to find the rtlsdr library");
                _applicationLifetime.StopApplication();
            }
            catch (BadImageFormatException)
            {
                _logger.LogCritical("The rtlsdr library or one of its dependencies has been built for the wrong system architecture");
                _applicationLifetime.StopApplication();
            }
        }

        /// <inheritdoc/>
        public void Stop()
        {
            // Check that the device has been started
            if (_device == IntPtr.Zero)
            {
                return;
            }

            // Log that the radio is stopping
            _logger.LogInformation($"Stopping the rtl-sdr radio ({Name})");

            // Stop reading samples from the device
            Interop.CancelAsync(_device);
            _receiverThread.Join();

            // Close the device
            Interop.Close(_device);

            // Clear the device handle and name
            _device = IntPtr.Zero;
            Name = string.Empty;

            // Log that the radio has stopped
            _logger.LogInformation($"The radio has stopped");
        }
        #endregion

        #region Sample handling methods
        /// <summary>
        /// Worker for the receiver thead. Starts the read functionality provided by the rtl-sdr library.
        /// </summary>
        private void ReceiverWorker()
        {
            // Reset the device sample buffer
            Interop.ResetBuffer(_device);

            // Start reading samples
            Interop.ReadAsync(_device, _readCallback, IntPtr.Zero, 0, 0);
        }

        /// <summary>
        /// The callback method called by the rtl-sdr library to provide received samples.
        /// </summary>
        /// <param name="buf">The buffer of samples.</param>
        /// <param name="len">The length of the buffer.</param>
        /// <param name="ctx">The user context passed to the read function.</param>
        private unsafe void ProcessSamples(byte* buf, uint len, IntPtr ctx)
        {
            // Wrap the buffer in a span
            Span<byte> buffer = new(buf, (int)len);

            // Copy the buffer to a new array of bytes in managed memory
            byte[] bufferArray = buffer.ToArray();

            // Fire the samples available event
            SamplesAvailable?.Invoke(this, bufferArray);
        }
        #endregion
    }
}
