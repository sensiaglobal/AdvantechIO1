using System;
using System.Runtime.InteropServices;
using FTOptix.HMIProject;
using FTOptix.UI;
using FTOptix.Retentivity;
using FTOptix.NativeUI;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.NetLogic;
using UAManagedCore;
using System.Threading;
using System.Collections.Generic;

namespace EAPI
{
    public class Eapi 
    {
        private PeriodicTask IOScan;
        private PeriodicTask ModelUpdate;
        private Mutex eapiDBMutex;
        public bool IsInitialized;
        public List<GPIOInfo> gpioInfoList;
        public List<DiscreteIOInfo> discreteIOsList;
        
        public Eapi (Config cfg){
            this.IsInitialized = false;
            this.gpioInfoList = new List<GPIOInfo>();
            this.discreteIOsList = new List<DiscreteIOInfo>();
            this.eapiDBMutex = new Mutex(false, cfg.mutexName);
        }

        public void StartScanTask(int period, IUAObject logicObject)
        {
                IOScan = new PeriodicTask(Scan, period, logicObject);
                IOScan.Start();
        }

        private void Scan(PeriodicTask task)
        {
            // Get control of the Mutex
            try
            {
                eapiDBMutex.WaitOne();
            }
            catch (Exception e)
            {
                Log.Error("Scan() - error trying to get hold of mutex. Error: " + e.Message);
            }
            //
            // I got the mutex. Do the Scan
            //
            GPIOPinRefresh();
            //
            // Return mutex
            //
            try
            {
                eapiDBMutex.ReleaseMutex();
            }
            catch (Exception e)
            {
                Log.Error ("Scan() - error trying to release mutex. Error: " + e.Message);
            }
        }
      
        public void LoadAll()
        {
            //Initialize
            this.gpioInfoList = new List<GPIOInfo>();
            this.discreteIOsList = new List<DiscreteIOInfo>();
            LibInitialize();
            LoadGPIO();
            //LoadHWMonitor();
            //LoadLED();
            //LoadRedundantPowerStatus();
            //LoadAuxIO();
            //LoadIPSAE();
            //LoadNetwork();
            this.IsInitialized = true;
        }

        public void LibInitialize()
        {
            try
            {
                uint status = Constants.EAPI_STATUS_SUCCESS;

                status = MethodClass.NativeMethods.EApiLibInitialize();
                if (status != Constants.EAPI_STATUS_SUCCESS)
                {
                    throw new Exception("Initialize failed !! Error code : 0x" + status.ToString("X8"));
                }
            }
            catch (Exception ex)
            {
                Log.Error("LibInitialize() - " +  ex.Message);
                throw;
            }
        }
        public bool GetLevel(byte iPin, uint idTypeVal, ref uint pValue)
                {
                bool ret = false;
                try
                {
                    uint status = Constants.EAPI_STATUS_ERROR, id = 0, mask = 0;
                    byte iBank = 0;
                    if (idTypeVal == (uint)GPIO.ID_TYPE.idtypeSingle)
                    {
                        id = iPin;
                        mask = 1;
                    }
                    else
                    {
                        iBank = (byte)(iPin >> 5);
                        id = GPIO.EAPI_ID_GPIO_BANK(iBank);
                        mask = GPIO.info[iBank].supInput | GPIO.info[iBank].supOutput;
                    }

                    status = MethodClass.NativeMethods.EApiGPIOGetLevel(id, mask, ref pValue);
                    if (status == Constants.EAPI_STATUS_SUCCESS)
                    {
                        ret = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                    throw;
                }
                return ret;
            }

            public bool SetLevel(byte iPin, string inputVal, uint idTypeVal)
            {
                GetLevel(iPin, GPIO.config.idType, ref GPIO.config.levelVal);
                uint status = Constants.EAPI_STATUS_ERROR, id = 0, mask = 0, setVal = GPIO.config.levelVal;
                byte iBank = 0;
                bool ret = false;

                try
                {
                    if (idTypeVal == (uint)GPIO.ID_TYPE.idtypeSingle)
                    {
                        id = iPin;
                        mask = 1;

                        // Check Number
                        uint tempVal = 0;
                        if (uint.TryParse(inputVal, out tempVal))
                        {
                            setVal = tempVal;
                            // Check 0 or 1
                            if ((setVal != 0) && (setVal != 1))
                            {
                                return ret;
                            }
                        }
                        else
                        {
                            return ret;
                        }
                    }
                    else
                    {
                        iBank = (byte)(iPin >> 5);
                        id = GPIO.EAPI_ID_GPIO_BANK(iBank);
                        mask = GPIO.info[iBank].supInput | GPIO.info[iBank].supOutput;

                        setVal = Convert.ToUInt32(inputVal, 16);
                    }

                    status = MethodClass.NativeMethods.EApiGPIOSetLevel(id, mask, setVal);

                    if (status == Constants.EAPI_STATUS_SUCCESS)
                    {
                        ret = true;
                    }
                    return ret;
                }
                catch (Exception ex)
                {
                    //MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Log.Error("SetLevel: " + ex.Message);
                    
                    throw;
                }
            }
    private void LoadGPIO()
        {
            if (EnumerateGPIO())
            {
                LoadDiscreteIOInfo();
            }
            else
            {
                throw new Exception("LoadGPIO() - GPIOa are Unavailable!!");
            }
        }
        private bool EnumerateGPIO()
        {
            uint status = Constants.EAPI_STATUS_ERROR, supportPin = 0, id = 0;
            byte index, i, j;
            try
            {
                index = 0;
                for (i = 0; i < GPIO.BANK_MAX; i++)
                {
                    id = GPIO.EAPI_ID_GPIO_BANK(i);
                    GPIOInfo gpioSingleInfo = new GPIOInfo();
                    status = MethodClass.NativeMethods.EApiGPIOGetDirectionCaps(id, ref gpioSingleInfo.supInput, ref gpioSingleInfo.supOutput);
                    if (status != Constants.EAPI_STATUS_SUCCESS)
                    {
                        continue;
                    }
                    supportPin = gpioSingleInfo.supInput |  gpioSingleInfo.supOutput;

                    if (supportPin > 0)
                    {
                        for (j = 32; (supportPin & (1 << (j - 1))) == 0; j--) {; }
                        gpioSingleInfo.supPinNum = j;
                    }
                    else
                    {
                        gpioSingleInfo.supPinNum = 0;
                    }                    
                    index++;
                    gpioInfoList.Add(gpioSingleInfo);
                }
                if (index == 0)
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                throw new Exception ("EnumerateGPIO() - " + ex.Message);
            }
            return true;
        }

        private void LoadDiscreteIOInfo()
        {
            //
            // Review that has been obtained by querying the hardware
            //
            foreach (GPIOInfo gi in gpioInfoList)
            {
                int di_count = 0;
                int do_count = 0;
                for (int i=0; i<gi.supPinNum; i++)
                {
                    DiscreteIOInfo dIOInfo = new DiscreteIOInfo();
                    dIOInfo.bitPosition = i;
                    uint si = (uint) gi.supInput;
                    uint so = (uint) gi.supOutput;
                    bool di_type = ((si >> i) & 1) == 1;
                    bool do_type = ((so >> i) & 1) == 1;
                    if ((di_type == true) && (do_type == false))
                    {
                        dIOInfo.ioType = IoType.dInput;
                        dIOInfo.num = di_count++;
                    }
                    else if ((di_type == false) && (do_type == true))
                    {
                        dIOInfo.ioType = IoType.dOutput;
                        dIOInfo.num = do_count++;
                    }
                    else
                    {
                        dIOInfo.ioType = IoType.unknown;
                    }
                    dIOInfo.value = 0;
                    dIOInfo.idType = GPIO.ID_TYPE.idtypeSingle;
                    discreteIOsList.Add(dIOInfo);
                }
            }
        }
        public void GPIOPinRefresh()
        {
            foreach (DiscreteIOInfo dio in discreteIOsList)
            {
                if (dio.ioType == IoType.dInput)
                {
                    uint val = 0;
                    GetLevel((byte)dio.bitPosition, (uint)dio.idType, ref val);
                    dio.value = val;
                }
                //else if (dio.ioType == IoType.dOutput)
                //{
                    //SetLevel((byte)dio.bitPosition, dio.value.ToString(), (uint)dio.idType);
                //}
            }

        }
    }
}