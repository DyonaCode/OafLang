#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include "atomic_ops.h"
#include "oaf_async.h"
#include "oaf_parallel.h"
#include "oaf_thread_pool.h"

typedef struct SumTaskState
{
    OafAtomicI64* accumulator;
    int64_t value;
} SumTaskState;

typedef struct AddState
{
    int left;
    int right;
    int result;
} AddState;

typedef struct FillState
{
    int64_t* output;
} FillState;

typedef struct MapState
{
    int64_t factor;
} MapState;

typedef struct ReduceState
{
    const int64_t* input;
} ReduceState;

static void sum_task(void* state)
{
    SumTaskState* task = (SumTaskState*)state;
    oaf_atomic_i64_fetch_add(task->accumulator, task->value);
}

static void* add_async(void* state)
{
    AddState* add = (AddState*)state;
    add->result = add->left + add->right;
    return &add->result;
}

static void fill_sequence(size_t index, void* state)
{
    FillState* fill = (FillState*)state;
    fill->output[index] = (int64_t)(index + 1);
}

static void map_scale(size_t index, const void* input, void* output, void* state)
{
    const int64_t* source = (const int64_t*)input;
    int64_t* destination = (int64_t*)output;
    MapState* map = (MapState*)state;
    (void)index;
    *destination = (*source) * map->factor;
}

static int64_t reduce_sum(size_t index, void* state)
{
    ReduceState* reduce = (ReduceState*)state;
    return reduce->input[index];
}

int main(void)
{
    OafThreadPool pool;
    OafAtomicI64 task_total;
    SumTaskState tasks[8];
    AddState add;
    OafFuture future;
    void* future_result = NULL;
    int64_t* sequence = NULL;
    int64_t* mapped = NULL;
    FillState fill;
    MapState map;
    ReduceState reduce;
    int64_t reduced_total = 0;
    const size_t count = 256;
    const int64_t expected_sequence_sum = ((int64_t)count * ((int64_t)count + 1)) / 2;
    size_t index;
    int ok = 1;

    if (!oaf_thread_pool_init(&pool, 4, 64))
    {
        fprintf(stderr, "failed to initialize thread pool\n");
        return 1;
    }

    oaf_atomic_i64_init(&task_total, 0);
    for (index = 0; index < 8; index++)
    {
        tasks[index].accumulator = &task_total;
        tasks[index].value = (int64_t)(index + 1);
        if (!oaf_thread_pool_submit(&pool, sum_task, &tasks[index]))
        {
            fprintf(stderr, "failed to submit task %zu\n", index);
            oaf_thread_pool_shutdown(&pool);
            return 1;
        }
    }

    if (!oaf_thread_pool_wait_idle(&pool) || oaf_atomic_i64_load(&task_total) != 36)
    {
        fprintf(stderr, "task pool accumulation failed\n");
        oaf_thread_pool_shutdown(&pool);
        return 1;
    }

    add.left = 19;
    add.right = 23;
    add.result = 0;
    if (!oaf_async_submit(&pool, add_async, &add, &future))
    {
        fprintf(stderr, "failed to submit async task\n");
        oaf_thread_pool_shutdown(&pool);
        return 1;
    }

    if (!oaf_future_await(&future, &future_result) || future_result != &add.result || *(int*)future_result != 42)
    {
        fprintf(stderr, "async/await result mismatch\n");
        oaf_future_destroy(&future);
        oaf_thread_pool_shutdown(&pool);
        return 1;
    }
    oaf_future_destroy(&future);

    sequence = (int64_t*)malloc(sizeof(int64_t) * count);
    mapped = (int64_t*)malloc(sizeof(int64_t) * count);
    if (sequence == NULL || mapped == NULL)
    {
        fprintf(stderr, "failed to allocate parallel arrays\n");
        free(sequence);
        free(mapped);
        oaf_thread_pool_shutdown(&pool);
        return 1;
    }

    fill.output = sequence;
    map.factor = 3;
    reduce.input = mapped;

    ok = ok && oaf_parallel_for(&pool, count, 0, fill_sequence, &fill);
    ok = ok && oaf_parallel_map(&pool, sequence, mapped, count, sizeof(int64_t), 0, map_scale, &map);
    ok = ok && oaf_parallel_reduce_i64(&pool, count, 0, reduce_sum, &reduce, &reduced_total);
    ok = ok && (reduced_total == (expected_sequence_sum * map.factor));

    free(sequence);
    free(mapped);
    oaf_thread_pool_shutdown(&pool);

    if (!ok)
    {
        fprintf(stderr, "parallel algorithm stage failed\n");
        return 1;
    }

    printf(
        "task + async + parallel example passed (task_sum=%lld, async=%d, reduced=%lld)\n",
        (long long)oaf_atomic_i64_load(&task_total),
        add.result,
        (long long)reduced_total);
    return 0;
}
