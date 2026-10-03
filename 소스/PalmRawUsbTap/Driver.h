#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <hidport.h>

typedef enum _PALM_DEVICE_KIND {
    PalmDeviceUnknown = 0,
    PalmDeviceTouch = 1,
    PalmDevicePen = 2,
    PalmDeviceVendorCol01 = 3
} PALM_DEVICE_KIND;

typedef struct _DEVICE_CONTEXT {
    WDFDEVICE Device;
    LONG DeviceId;
    PALM_DEVICE_KIND Kind;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(DEVICE_CONTEXT, DeviceGetContext)

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD EvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEFAULT EvtIoDefault;
EVT_WDF_REQUEST_COMPLETION_ROUTINE EvtInputReportCompletion;

BOOLEAN PalmIsInputReportRequest(_In_ PWDF_REQUEST_PARAMETERS Params);
VOID PalmDetectDeviceKind(_In_ WDFDEVICE Device, _Inout_ PDEVICE_CONTEXT Ctx);
BOOLEAN PalmMultiSzContainsAsciiInsensitive(_In_reads_bytes_(Bytes) PCWSTR MultiSz, _In_ size_t Bytes, _In_z_ PCWSTR Needle);
LONGLONG PalmGetPenDelta100ns(VOID);
VOID PalmLogFirstBytes(_In_z_ PCSTR Prefix, _In_ WDFREQUEST Request, _In_ ULONG_PTR Information);
VOID PalmMarkPenActivity(_In_ WDFREQUEST Request, _In_ ULONG_PTR Information);
VOID PalmLogPenFlags(_In_ WDFREQUEST Request, _In_ ULONG_PTR Information, _In_ LONG Count, _In_ LONGLONG IntervalUs);
BOOLEAN PalmShouldBlockTouch(VOID);
BOOLEAN PalmTouchReportLooksActive(_In_ WDFREQUEST Request, _In_ ULONG_PTR Information);
VOID PalmUpdateTouchActivity(_In_ WDFREQUEST Request, _In_ ULONG_PTR Information, _In_ LONG DeviceId);
VOID PalmMakePenReportHoverLike(_Inout_updates_bytes_(Length) PUCHAR b, _In_ ULONG_PTR Length);
VOID PalmPatchHoverCoordinatesFromTipReport(_Inout_updates_bytes_(HoverLength) PUCHAR HoverReport, _In_ ULONG_PTR HoverLength, _In_reads_bytes_(TipLength) PUCHAR TipReport, _In_ ULONG_PTR TipLength);
VOID PalmStoreRealHoverReport(_In_reads_bytes_(Length) PUCHAR Report, _In_ ULONG_PTR Length, _In_ LONGLONG Now100ns);
VOID PalmClearSyntheticPenReport(_In_z_ PCSTR Reason);
BOOLEAN PalmSyntheticPenReportPending(VOID);
VOID PalmQueueSyntheticPenReport(_In_reads_bytes_(Length) PUCHAR Report, _In_ ULONG_PTR Length, _In_ LONGLONG Now100ns, _In_ BOOLEAN ForceSingleSettle);
BOOLEAN PalmTryCompleteSyntheticPenReport(_In_ WDFREQUEST Request);
VOID PalmMaybeHoldPenTipUntilTouchOwnershipTransfers(_In_ WDFREQUEST Request, _In_ ULONG_PTR Information);
VOID PalmZeroInputReport(_In_ WDFREQUEST Request, _In_ PWDF_REQUEST_COMPLETION_PARAMS Params);
