#ifndef OAFLANG_RUNTIME_H
#define OAFLANG_RUNTIME_H

#include <stddef.h>
#include "context.h"
#include "error.h"
#include "stack_trace.h"
#include "default_allocator.h"
#include "temp_allocator.h"
#include "gc.h"
#include "type_info.h"
#include "scheduler.h"

#ifdef __cplusplus
extern "C" {
#endif

#define OAF_RUNTIME_DEFAULT_TEMP_CAPACITY (64u * 1024u)

typedef struct OafRuntimeOptions
{
    size_t temp_allocator_capacity;
    size_t scheduler_worker_count;
    int gc_enabled;
} OafRuntimeOptions;

typedef enum OafRuntimeStatus
{
    OAF_RUNTIME_STATUS_OK = 0,
    OAF_RUNTIME_STATUS_ALREADY_INITIALIZED = 1,
    OAF_RUNTIME_STATUS_INIT_FAILED = 2,
    OAF_RUNTIME_STATUS_INVALID_ARGUMENT = 3
} OafRuntimeStatus;

typedef struct OafRuntime
{
    OafDefaultAllocatorState default_allocator_state;
    OafAllocator default_allocator;
    OafTempAllocatorState temp_allocator_state;
    OafThreadScheduler scheduler;
    OafGarbageCollector gc;
    OafTypeRegistry type_registry;
    OafStackTrace stack_trace;
    OafContext context;
    OafRuntimeError startup_error;
    int initialized;
} OafRuntime;

void oaf_runtime_options_default(OafRuntimeOptions* options);
OafRuntimeStatus oaf_runtime_init(OafRuntime* runtime, const OafRuntimeOptions* options);
void oaf_runtime_shutdown(OafRuntime* runtime);
OafContext* oaf_runtime_context(OafRuntime* runtime);
OafThreadScheduler* oaf_runtime_scheduler(OafRuntime* runtime);
OafGarbageCollector* oaf_runtime_gc(OafRuntime* runtime);
OafTypeRegistry* oaf_runtime_type_registry(OafRuntime* runtime);
const OafRuntimeError* oaf_runtime_last_error(const OafRuntime* runtime);

int oaf_runtime_version(void);

#ifdef __cplusplus
}
#endif

#endif
