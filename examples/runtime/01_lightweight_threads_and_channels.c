#include <stdint.h>
#include <stdio.h>
#include "atomic_ops.h"
#include "channel.h"
#include "scheduler.h"

typedef struct SumTaskState
{
    OafAtomicI64* accumulator;
    int64_t value;
} SumTaskState;

static void* accumulate_task(void* args)
{
    SumTaskState* task = (SumTaskState*)args;
    oaf_atomic_i64_fetch_add(task->accumulator, task->value);
    return NULL;
}

int main(void)
{
    OafThreadScheduler scheduler;
    OafChannel channel;
    OafAtomicI64 sum;
    OafLightweightThread* threads[4];
    SumTaskState tasks[4];
    int first = 7;
    int second = 11;
    void* received = NULL;
    size_t index;

    if (!oaf_scheduler_init(&scheduler, 2))
    {
        fprintf(stderr, "failed to initialize scheduler\n");
        return 1;
    }

    oaf_atomic_i64_init(&sum, 0);
    for (index = 0; index < 4; index++)
    {
        tasks[index].accumulator = &sum;
        tasks[index].value = (int64_t)(index + 1);
        threads[index] = oaf_scheduler_spawn(&scheduler, accumulate_task, &tasks[index]);
        if (threads[index] == NULL)
        {
            fprintf(stderr, "failed to spawn lightweight thread %zu\n", index);
            oaf_scheduler_shutdown(&scheduler);
            return 1;
        }
    }

    if (oaf_scheduler_run_all(&scheduler) != 4)
    {
        fprintf(stderr, "scheduler did not execute all lightweight threads\n");
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    if (oaf_atomic_i64_load(&sum) != 10)
    {
        fprintf(stderr, "unexpected lightweight thread sum\n");
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    for (index = 0; index < 4; index++)
    {
        if (!oaf_lightweight_thread_is_done(threads[index]))
        {
            fprintf(stderr, "thread %zu did not complete\n", index);
            oaf_scheduler_shutdown(&scheduler);
            return 1;
        }
    }

    if (!oaf_channel_init(&channel, 2))
    {
        fprintf(stderr, "failed to initialize channel\n");
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    if (!oaf_channel_try_send(&channel, &first) || !oaf_channel_try_send(&channel, &second))
    {
        fprintf(stderr, "failed to send channel values\n");
        oaf_channel_destroy(&channel);
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    if (!oaf_channel_recv(&channel, &received) || received != &first)
    {
        fprintf(stderr, "channel first receive mismatch\n");
        oaf_channel_destroy(&channel);
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    if (!oaf_channel_recv(&channel, &received) || received != &second)
    {
        fprintf(stderr, "channel second receive mismatch\n");
        oaf_channel_destroy(&channel);
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    oaf_channel_close(&channel);
    if (oaf_channel_recv(&channel, &received))
    {
        fprintf(stderr, "channel receive should fail after close\n");
        oaf_channel_destroy(&channel);
        oaf_scheduler_shutdown(&scheduler);
        return 1;
    }

    oaf_channel_destroy(&channel);
    oaf_scheduler_shutdown(&scheduler);

    printf("lightweight thread + channel example passed (sum=%lld)\n", (long long)oaf_atomic_i64_load(&sum));
    return 0;
}
