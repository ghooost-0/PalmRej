#pragma once

#include <ntddk.h>
#include <wdf.h>
#include <hidport.h>

#define PV1B_VERSION_TEXT "PV1K"
#define PV1B_DRIVER_VERSION_MAJOR 1
#define PV1B_DRIVER_VERSION_MINOR 11

#define PV1B_REPORT_BYTES 64u
#define PV1F_MAX_IDLE_BATCH_REPORTS 10u
#define PV1B_REPORT_ID_TOUCH 0x81u
#define PV1B_SCAN_TIME_LOW_OFFSET 61u
#define PV1B_SCAN_TIME_HIGH_OFFSET 62u
#define PV1B_CONTACT_COUNT_OFFSET 63u
#define PV1B_CONTACT_SLOT_COUNT 10u
#define PV1B_CONTACT_SLOT_BYTES 6u
#define PV1B_CONTACT_SLOT_BASE_OFFSET 1u
#define PV1B_STATE_TIP_BIT 0x01u
#define PV1B_STATE_CONFIDENCE_BIT 0x02u
#define PV1B_STATE_UNUSED_SENTINEL 0xFFu

#define PV1E_FUTURE_VETO_REASON_CONFIDENCE_LOSS 0x01u
#define PV1E_FUTURE_VETO_REASON_THREEPLUS 0x02u
#define PV1H_FUTURE_VETO_REASON_PARTIAL_CONFIDENCE_BOUNDARY 0x04u

#define PV1B_DEFERRED_SLOTS 7u
#define PV1B_TARGET_REPORT_DEPTH 8u
#define PV1B_LOG_LIMIT 32768LL

// PV0112: FatalBypass is a bit set; every test of it is still "!= 0".
// ON alone is recoverable and is left at the next all-up report the reader
// receives through the bypass. ON|STICKY keeps the PV1K latch: only
// EvtDeviceD0Entry clears it.
#define PV0112_BYPASS_ON 0x1L
#define PV0112_BYPASS_STICKY 0x2L

typedef enum _PV1B_DEVICE_ROLE
{
    Pv1bRoleTouchCol02 = 1
} PV1B_DEVICE_ROLE;

typedef enum _PV1B_REQUEST_PHASE
{
    Pv1bRequestPhaseIdle = 0,
    Pv1bRequestPhaseFirstSend = 1,
    Pv1bRequestPhaseLookaheadArmed = 2,
    Pv1bRequestPhaseLookaheadInFlight = 3,
    Pv1bRequestPhaseLookaheadComplete = 4,
    Pv1bRequestPhaseDeferredDelivery = 5
} PV1B_REQUEST_PHASE;

typedef struct _PV1B_REQUEST_CONTEXT
{
    WDFDEVICE Device;
    LONG64 CycleId;
    volatile LONG Phase;
    volatile LONG UpperCompletionIssued;
    volatile LONG DeviceReferenceHeld;

    ULONG_PTR FirstInformation;
    UCHAR FirstReport[PV1B_REPORT_BYTES];

    ULONG CurrentPhysicalIndex;
    ULONG StoredLookaheadCount;

    // PV1E: request-local evidence is observed during report 1..8 only.
    // It becomes policy only after a safe commit point (boundary store or full FIFO store).
    volatile LONG FutureVetoCandidate;
    volatile LONG FutureVetoSuppressFirst;
    ULONG FutureVetoReasonMask;
    ULONG FutureVetoFirstPhysicalIndex;

    ULONG_PTR LookaheadInformation[PV1B_DEFERRED_SLOTS];
    UCHAR LookaheadReport[PV1B_DEFERRED_SLOTS][PV1B_REPORT_BYTES];
} PV1B_REQUEST_CONTEXT, *PPV1B_REQUEST_CONTEXT;

typedef struct _PV1B_COUNTERS
{
    volatile LONG64 DirectForwards;
    volatile LONG64 DirectForwardFailures;

    volatile LONG64 InputCandidates;
    volatile LONG64 CyclesStarted;

    volatile LONG64 FirstSendAttempts;
    volatile LONG64 FirstSendFailures;
    volatile LONG64 FirstCompletions;
    volatile LONG64 FirstCompletionFailures;
    volatile LONG64 FirstActiveReports;
    volatile LONG64 FirstCanceledBeforeReuse;

    volatile LONG64 LookaheadReuseArmed;
    volatile LONG64 LookaheadReuseAttempts;
    volatile LONG64 LookaheadReuseSuccesses;
    volatile LONG64 LookaheadReuseFailures;

    volatile LONG64 LookaheadSendArmed;
    volatile LONG64 LookaheadSendAttempts;
    volatile LONG64 LookaheadSendInFlight;
    volatile LONG64 LookaheadSendFailures;

    volatile LONG64 LookaheadCompletions;
    volatile LONG64 LookaheadCompletionSuccesses;
    volatile LONG64 LookaheadCompletionFailures;
    volatile LONG64 LookaheadCompletionCanceled;

    volatile LONG64 LookaheadFileObjectBeforeReuseNonNull;
    volatile LONG64 LookaheadFileObjectAfterReuseNonNull;
    volatile LONG64 LookaheadFileObjectAfterFormatNonNull;
    volatile LONG64 LookaheadFileObjectPreserved;

    volatile LONG64 DeferredStores;
    volatile LONG64 DeferredDeliveries;
    volatile LONG64 DeferredBufferFailures;
    volatile LONG64 DeferredCollisionFailures;

    volatile LONG64 RollingPhysicalArrivals;
    volatile LONG64 RollingOutputs;
    volatile LONG64 RollingAllNonactiveClears;
    volatile LONG64 RollingBoundaryFlushes;
    volatile LONG64 BoundaryActiveTailDrops;
    volatile LONG64 StaleFifoPurgesBeforeNewEpisode;
    volatile LONG64 RollingInvariantFailures;
    volatile LONG64 RollingCadenceViolations;

    volatile LONG64 BoundaryReleaseStores;
    volatile LONG64 BoundaryReleaseDeliveries;
    volatile LONG64 BoundaryReleaseInvariantFailures;

    volatile LONG64 UpperCompletions;
    volatile LONG64 UpperCanceledCompletions;
    volatile LONG64 DoubleCompletePrevented;

    volatile LONG64 CancelSentAttempts;
    volatile LONG64 CancelSentAccepted;
    volatile LONG64 CancelSentRejected;

    volatile LONG64 EpisodeStarts;
    volatile LONG64 EpisodeEnds;
    volatile LONG64 EpisodeStartInvariantFailures;
    volatile LONG64 EpisodeBoundaryStaleFifoFailures;
    volatile LONG64 TailDrainCompletions;
    volatile LONG64 TeardownDeferredDrops;

    volatile LONG64 FutureVetoCandidates;
    volatile LONG64 FutureVetoBoundaryCommits;
    volatile LONG64 FutureVetoFullDepthCommits;
    volatile LONG64 FutureVetoSuppressedFirstReports;
    volatile LONG64 FutureVetoSuppressedRollingReports;

    volatile LONG64 FatalBypassEvents;

    volatile LONG64 PrepareHardwareCalls;
    volatile LONG64 ReleaseHardwareCalls;
    volatile LONG64 D0EntryCalls;
    volatile LONG64 D0ExitCalls;
    volatile LONG64 SelfManagedInitCalls;
    volatile LONG64 SelfManagedSuspendCalls;
    volatile LONG64 SelfManagedRestartCalls;
    volatile LONG64 SelfManagedCleanupCalls;
    volatile LONG64 SurpriseRemovalCalls;
    volatile LONG64 ContextCleanupCalls;
} PV1B_COUNTERS, *PPV1B_COUNTERS;

typedef struct _DEVICE_CONTEXT
{
    LONG DeviceId;
    PV1B_DEVICE_ROLE Role;

    WDFQUEUE DefaultQueue;
    WDFSPINLOCK StateLock;

    WDFREQUEST ActiveLookaheadRequest;

    UCHAR DeferredReport[PV1B_DEFERRED_SLOTS][PV1B_REPORT_BYTES];
    ULONG_PTR DeferredInformation[PV1B_DEFERRED_SLOTS];
    ULONG DeferredHead;

    // Historical field name retained from PV0W to minimize lifecycle delta.
    // In PV1D this is a COUNT in the range 0..7, not a Boolean.
    volatile LONG DeferredValid;

    // Exactly one real non-active report may be retained after an initial
    // warm-up boundary so the first active report and its release both reach
    // HIDClass without retaining any active tail from the closed episode.
    volatile LONG BoundaryReleasePending;

    // PV1J: exact bytes from the most recent successful TOUCH report completed
    // upward to HIDClass. This is intentionally separate from DeferredReport:
    // DeferredHead points to the NEXT delayed report, not the last visible one.
    UCHAR LastUpperTouchReport[PV1B_REPORT_BYTES];
    ULONG_PTR LastUpperTouchInformation;
    volatile LONG LastUpperTouchValid;

    volatile LONG64 LastRollingOutputQpc;

    volatile LONG PhysicalEpisodeActive;

    // PV1E latch: set only after the initial depth-8 FIFO has been stored.
    // Raw FIFO contents remain unchanged; only upper-visible active outputs are zeroed.
    volatile LONG FutureVetoActive;
    volatile LONG FutureVetoReasonMask;

    volatile LONG64 EpisodeId;

    volatile LONG FatalBypass;
    volatile LONG HardwarePrepared;
    volatile LONG DeviceInD0;
    volatile LONG SelfManagedActive;
    volatile LONG SurpriseRemoved;
    volatile LONG Generation;

    PV1B_COUNTERS Counters;
} DEVICE_CONTEXT, *PDEVICE_CONTEXT;

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(
    DEVICE_CONTEXT,
    DeviceGetContext
    );

WDF_DECLARE_CONTEXT_TYPE_WITH_NAME(
    PV1B_REQUEST_CONTEXT,
    RequestGetContext
    );

DRIVER_INITIALIZE DriverEntry;
EVT_WDF_DRIVER_DEVICE_ADD EvtDeviceAdd;
EVT_WDF_IO_QUEUE_IO_DEFAULT EvtIoDefault;

EVT_WDF_REQUEST_COMPLETION_ROUTINE EvtPv1bFirstCompletion;
EVT_WDF_REQUEST_COMPLETION_ROUTINE EvtPv1bLookaheadCompletion;
EVT_WDF_REQUEST_COMPLETION_ROUTINE EvtPv0112BypassCompletion;

EVT_WDF_DEVICE_PREPARE_HARDWARE EvtDevicePrepareHardware;
EVT_WDF_DEVICE_RELEASE_HARDWARE EvtDeviceReleaseHardware;
EVT_WDF_DEVICE_D0_ENTRY EvtDeviceD0Entry;
EVT_WDF_DEVICE_D0_EXIT EvtDeviceD0Exit;
EVT_WDF_DEVICE_SELF_MANAGED_IO_INIT EvtDeviceSelfManagedIoInit;
EVT_WDF_DEVICE_SELF_MANAGED_IO_SUSPEND EvtDeviceSelfManagedIoSuspend;
EVT_WDF_DEVICE_SELF_MANAGED_IO_RESTART EvtDeviceSelfManagedIoRestart;
EVT_WDF_DEVICE_SELF_MANAGED_IO_CLEANUP EvtDeviceSelfManagedIoCleanup;
EVT_WDF_DEVICE_SURPRISE_REMOVAL EvtDeviceSurpriseRemoval;
EVT_WDF_OBJECT_CONTEXT_CLEANUP EvtDeviceContextCleanup;
