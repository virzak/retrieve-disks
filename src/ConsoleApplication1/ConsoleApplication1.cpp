// ConsoleApplication1.cpp : This file contains the 'main' function. Program execution begins and ends there.
//

#include <Windows.h>
#include <Setupapi.h>
#include <Ntddstor.h>

#pragma comment( lib, "setupapi.lib" )

#include <iostream>
#include <string>
using namespace std;

#define START_ERROR_CHK()           \
    DWORD error = ERROR_SUCCESS;    \
    DWORD failedLine;               \
    string failedApi;

#define CHK( expr, api )            \
    if ( !( expr ) ) {              \
        error = GetLastError( );    \
        failedLine = __LINE__;      \
        failedApi = ( api );        \
        goto Error_Exit;            \
    }

#define END_ERROR_CHK()             \
    error = ERROR_SUCCESS;          \
    Error_Exit:                     \
    if ( ERROR_SUCCESS != error ) { \
        cout << failedApi << " failed at " << failedLine << " : Error Code - " << error << endl;    \
    }

// https://stackoverflow.com/a/48250301/6461844
ULONG GetSerial(HANDLE hFile)
{
    static STORAGE_PROPERTY_QUERY spq = { StorageDeviceProperty, PropertyStandardQuery };

    union {
        PVOID buf;
        PSTR psz;
        PSTORAGE_DEVICE_DESCRIPTOR psdd;
    };

    ULONG size = sizeof(STORAGE_DEVICE_DESCRIPTOR) + 0x100;

    ULONG dwError;

    do
    {
        dwError = ERROR_NO_SYSTEM_RESOURCES;

        if (buf = LocalAlloc(0, size))
        {
            ULONG BytesReturned;

            if (DeviceIoControl(hFile, IOCTL_STORAGE_QUERY_PROPERTY, &spq, sizeof(spq), buf, size, &BytesReturned, 0))
            {
                if (psdd->Version >= sizeof(STORAGE_DEVICE_DESCRIPTOR))
                {
                    if (psdd->Size > size)
                    {
                        size = psdd->Size;
                        dwError = ERROR_MORE_DATA;
                    }
                    else
                    {
                        if (psdd->SerialNumberOffset)
                        {
                            //DbgPrint("SerialNumber = %s\n", psz + psdd->SerialNumberOffset);
                            cout << "SerialNumber = " << psz + psdd->SerialNumberOffset << endl;
                            dwError = NOERROR;
                        }
                        else
                        {
                            dwError = ERROR_NO_DATA;
                        }
                    }
                }
                else
                {
                    dwError = ERROR_GEN_FAILURE;
                }
            }
            else
            {
                dwError = GetLastError();
            }

            LocalFree(buf);
        }
    } while (dwError == ERROR_MORE_DATA);

    return dwError;
}

int main()
{
    HDEVINFO diskClassDevices;
    GUID diskClassDeviceInterfaceGuid = GUID_DEVINTERFACE_DISK;
    SP_DEVICE_INTERFACE_DATA deviceInterfaceData;
    PSP_DEVICE_INTERFACE_DETAIL_DATA deviceInterfaceDetailData;
    DWORD requiredSize;
    DWORD deviceIndex;
    BOOL hahaha;

    HANDLE disk = INVALID_HANDLE_VALUE;
    STORAGE_DEVICE_NUMBER diskNumber;
    DWORD bytesReturned;

    START_ERROR_CHK();

    //
    // Get the handle to the device information set for installed
    // disk class devices. Returns only devices that are currently
    // present in the system and have an enabled disk device
    // interface.
    //
    diskClassDevices = SetupDiGetClassDevs(&diskClassDeviceInterfaceGuid,
        NULL,
        NULL,
        DIGCF_PRESENT |
        DIGCF_DEVICEINTERFACE);
    CHK(INVALID_HANDLE_VALUE != diskClassDevices,
        "SetupDiGetClassDevs");

    ZeroMemory(&deviceInterfaceData, sizeof(SP_DEVICE_INTERFACE_DATA));
    deviceInterfaceData.cbSize = sizeof(SP_DEVICE_INTERFACE_DATA);
    deviceIndex = 0;

    while (SetupDiEnumDeviceInterfaces(diskClassDevices,
        NULL,
        &diskClassDeviceInterfaceGuid,
        deviceIndex,
        &deviceInterfaceData)) {

        ++deviceIndex;

        SetupDiGetDeviceInterfaceDetail(diskClassDevices,
            &deviceInterfaceData,
            NULL,
            0,
            &requiredSize,
            NULL);
        CHK(ERROR_INSUFFICIENT_BUFFER == GetLastError(),
            "SetupDiGetDeviceInterfaceDetail - 1");

        deviceInterfaceDetailData = (PSP_DEVICE_INTERFACE_DETAIL_DATA)malloc(requiredSize);
        CHK(NULL != deviceInterfaceDetailData,
            "malloc");

        ZeroMemory(deviceInterfaceDetailData, requiredSize);
        deviceInterfaceDetailData->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA);

        hahaha = SetupDiGetDeviceInterfaceDetail(diskClassDevices,
            &deviceInterfaceData,
            deviceInterfaceDetailData,
            requiredSize,
            NULL,
            NULL);

        CHK(SetupDiGetDeviceInterfaceDetail(diskClassDevices,
            &deviceInterfaceData,
            deviceInterfaceDetailData,
            requiredSize,
            NULL,
            NULL),
            "SetupDiGetDeviceInterfaceDetail - 2");

        disk = CreateFile(deviceInterfaceDetailData->DevicePath,
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL,
            OPEN_EXISTING,
            FILE_ATTRIBUTE_NORMAL,
            NULL);
        CHK(INVALID_HANDLE_VALUE != disk,
            "CreateFile");

        CHK(DeviceIoControl(disk,
            IOCTL_STORAGE_GET_DEVICE_NUMBER,
            NULL,
            0,
            &diskNumber,
            sizeof(STORAGE_DEVICE_NUMBER),
            &bytesReturned,
            NULL),
            "IOCTL_STORAGE_GET_DEVICE_NUMBER");

        GetSerial(disk);

        CloseHandle(disk);
        disk = INVALID_HANDLE_VALUE;

        cout << deviceInterfaceDetailData->DevicePath << endl;
        cout << "\\\\?\\PhysicalDrive" << diskNumber.DeviceNumber << endl;
        cout << endl;
    }
    CHK(ERROR_NO_MORE_ITEMS == GetLastError(),
        "SetupDiEnumDeviceInterfaces");

    END_ERROR_CHK();

Exit:

    if (INVALID_HANDLE_VALUE != diskClassDevices) {
        SetupDiDestroyDeviceInfoList(diskClassDevices);
    }

    if (INVALID_HANDLE_VALUE != disk) {
        CloseHandle(disk);
    }

    return error;
}

// Run program: Ctrl + F5 or Debug > Start Without Debugging menu
// Debug program: F5 or Debug > Start Debugging menu

// Tips for Getting Started: 
//   1. Use the Solution Explorer window to add/manage files
//   2. Use the Team Explorer window to connect to source control
//   3. Use the Output window to see build output and other messages
//   4. Use the Error List window to view errors
//   5. Go to Project > Add New Item to create new code files, or Project > Add Existing Item to add existing code files to the project
//   6. In the future, to open this project again, go to File > Open > Project and select the .sln file
