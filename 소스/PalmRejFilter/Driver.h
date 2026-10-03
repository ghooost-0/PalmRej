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

    /*
     * A475 self-owned READ probe.  Per device instance, so it cannot outlive
     * the device it belongs to - the tablet is power cycled repeatedly and
     * the driver image survives every removal, which is what broke the
     * previous revision.
     *
     * Everything is allocated once in EvtDeviceAdd, off the read path.  The
     * read path only formats and sends.
     */
    volatile LONG A475State;
    volatile LONG A475ProbeState;
    volatile LONG A475Seq;

    WDFREQUEST A475Request;
    PVOID A475Buffer;
    PMDL A475Mdl;

    /*
     * A476 proxy transport test.
     *
     * The intercepted upper READ waits in A476ProxyUpperQueue, a manual
     * queue, so the framework owns it while it waits and answers a cancel
     * without us.  A driver-owned request cannot be forwarded once it is
     * marked cancelable, so holding it in a raw pointer field would leave
     * nobody to answer a cancel arriving during the wait.
     *
     * One lower request, reused between cycles.  Never two: the measured
     * upper depth on this stack is exactly 1, and a second outstanding
     * lower read would reopen the frame-ownership question this build
     * exists to close.
     */
    WDFQUEUE A476ProxyUpperQueue;

    /*
     * A484 parks one upper READ here while a confirmed drag session is
     * running, and the bridge callback completes it when the next frame
     * arrives.  Separate from A476's queue on purpose: A476 is disarmed and
     * has its own retrieve paths, and sharing one queue would couple two
     * features that have no reason to know about each other.
     */
    WDFQUEUE A484ParkQueue;
    WDFREQUEST A476LowerRequest;
    PVOID A476LowerBuffer;
    PMDL A476LowerMdl;
    PFILE_OBJECT A476FileObject;

    volatile LONG A476Ready;
    volatile LONG A476Armed;
    volatile LONG A476SelfReadOutstanding;
    volatile LONG A476AwaitNextUpper;

    /*
     * A476R2 burst state.
     *
     * A476LowerReady is set by the completion routine BEFORE it completes
     * the held upper request.  Completing that request can re-enter
     * EvtIoDefault on this very thread, and the next cycle starts there -
     * so the lower request has to be reusable by then, not after the
     * completion routine finally returns.
     *
     * A476InlineDepth counts how deep that re-entry has stacked.  A lower
     * read that completes inline would otherwise let cycles nest without
     * bound and run the kernel stack out.
     */
    volatile LONG A476LowerReady;
    volatile LONG A476Remaining;
    volatile LONG A476Accepted;
    volatile LONG A476Completed;
    volatile LONG A476InlineDepth;
    volatile LONG A476MaxInlineDepth;
    volatile LONG A476Cutoff;
    volatile LONG A476Ended;
    volatile LONG A476NextUpperCount;

    volatile LONG64 A476Generation;
    volatile LONG64 A476QueuedGeneration;

    /*
     * A492R1 hold state.
     *
     * A492Retries counts how many times the self-read has been re-sent
     * without the held upper being completed.  A timed-out read on an idle
     * screen is normal and retries forever; nothing here bails out on time
     * alone, because the native read pends forever too.
     *
     * A492Strikes is the only thing that does bail out, and it needs
     * evidence rather than patience: a timeout that expired while the wire
     * tap was carrying frames means the router had data and did not give it
     * to a read this driver created.  Two of those and the hold gives up and
     * hands the collection back to the native path for good.
     *
     * A492SendBridgeSeq is the bridge sequence at the moment the self-read
     * went down, which is what the count above is measured against.
     */
    volatile LONG A492Retries;
    volatile LONG A492Strikes;
    volatile LONG64 A492SendBridgeSeq;
    /* 0.1.6: g_V016FingerWire at the same moment - see A492 in Driver.c. */
    volatile LONG64 V016SendFingerWire;
    /* 0.1.21: g_V0121PenFingerWire at the same moment - log only. */
    volatile LONG64 V0121SendPenWire;
    /*
     * A503R3: how many times a held upper had been spent by the tap or drag
     * relay when this collection's self-read was last accounted for.  If the
     * global count has moved since, the self-read's upper was taken on
     * purpose and its timeout is not evidence of anything.
     */
    volatile LONG64 A476SpentSeen;

    /*
     * 0.1.18 (V0118 ADOPT): 1 from PalmV0118AdoptOrphan's empty-queue test
     * until it returns, so two uppers can never both be handed to one
     * orphaned self-read.  Nothing else reads it.
     */
    volatile LONG V0118Adopting;

    /*
     * A493R1: which arm generation this device gave up at.
     *
     * EndBurst is a permanent latch per device, which was right when the only
     * way to change the switch was a power cycle - the device went away and
     * took the latch with it.  Now the switch can be turned off and on again
     * while the same device stays up, and a device that gave up under the
     * previous generation deserves a fresh start under the next one.
     */
    volatile LONG A493EndedGeneration;

    /*
     * A495R2: this collection's own settings poll.
     *
     * R1 kept one timer for the whole driver, claimed by the first touch
     * collection to be prepared and released by a cleanup callback when that
     * collection died, on the assumption that another device add would follow
     * and re-create it.  The real ordering is the other way round - measured
     * on a tablet power cycle, ids 6 and 7 were prepared at 31.617 s while id
     * 2 was still alive and holding the claim, and id 2 only died at 31.911 s
     * with nobody left to take over.  The poll stopped for good and the switch
     * never reached the driver again.
     *
     * So every touch collection now runs its own.  Four cheap registry reads
     * every two seconds instead of one, and the poll survives as long as any
     * one collection does.  Nothing is shared, so nothing has to be handed on.
     */
    WDFTIMER A493Timer;
    WDFWORKITEM A493WorkItem;

    /*
     * A502R1: the sweep that closes a segment nothing else will close.
     *
     * Per collection like the poll above, for the same reason - a driver-wide
     * one handed between devices is what stopped dead on 09-09.  Dispatch
     * level is fine here: unlike the settings poll this touches no registry,
     * only segment state under its own spin lock.
     */
    WDFTIMER A502SweepTimer;


    /*
     * A496R1: give-up bookkeeping, so a hold that ended can come back.
     *
     * Until now EndBurst was permanent for the life of the device and only an
     * off/on of the registry switch could undo it.  A tablet power cycle tears
     * the collection down mid-cycle, the completion routine sees
     * STATUS_DEVICE_NOT_CONNECTED, and the hold ends - for good.  A whole
     * evening's run went that way: the hold was dead the entire session, every
     * read went down natively, and the old A469 cancel fired thirteen times to
     * break the blackouts instead.
     *
     * A496EndedAt100ns is when it gave up and A496Retryable says whether the
     * reason was transient.  A transient give-up is retried after a cooldown;
     * SELF-READ-IGNORED is not, because that one means the router will not
     * answer a read this driver creates, and retrying only costs another
     * blackout to learn the same thing.
     */
    volatile LONG64 A496EndedAt100ns;
    volatile LONG A496Retryable;
    volatile LONG A496Revivals;

    /*
     * A509R1: how long this particular ending has to sit out, in
     * microseconds.  Written by PalmA476EndBurst from the reason, read by
     * PalmA493HoldAdmit.  Zero means the next read may take the hold back,
     * which is the right answer when the wire was not carrying anything -
     * nobody was being denied a report, so nothing was wrong to begin with.
     */
    volatile LONG64 A509ReviveAfterUs;




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
