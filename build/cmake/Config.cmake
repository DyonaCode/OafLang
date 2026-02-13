set(OAF_RUNTIME_SOURCES
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/src/runtime_stub.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/src/context.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/src/bootstrap.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/src/shutdown.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/error/src/error.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/error/src/stack_trace.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/types/src/type_info.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/types/src/reflection.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/types/src/interface_dispatch.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/src/thread.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/src/scheduler.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/src/sync_primitives.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/src/channel.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/src/atomic_ops.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/src/foreign_types.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/src/marshalling.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/src/callback_registry.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/src/ffi_runtime.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/allocator.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/allocators/default_allocator.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/allocators/arena_allocator.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/allocators/pool_allocator.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/allocators/temp_allocator.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/ownership.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/lifetime.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/bounds.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/null_safety.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/leak_detector.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/src/gc.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/algorithms/algorithms.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/concurrent/thread_pool.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/concurrent/async.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/concurrent/parallel.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/array.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/list.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/dict.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/set.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/io/file.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/io/stream.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/text/string.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/text/format.c
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/serialization/serializer.c
)

add_library(oaf_runtime STATIC ${OAF_RUNTIME_SOURCES})

target_include_directories(
    oaf_runtime
    PUBLIC
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/error/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/types/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/algorithms/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/concurrent/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/io/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/text/include
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/serialization/include
)

target_compile_features(oaf_runtime PUBLIC c_std_11)

find_package(Threads REQUIRED)
target_link_libraries(oaf_runtime PUBLIC Threads::Threads)

find_package(PkgConfig QUIET)
if(PKG_CONFIG_FOUND)
    pkg_check_modules(LIBFFI QUIET libffi)
endif()

if(LIBFFI_FOUND)
    target_compile_definitions(oaf_runtime PUBLIC OAF_HAVE_LIBFFI=1)
    target_include_directories(oaf_runtime PUBLIC ${LIBFFI_INCLUDE_DIRS})
    target_link_libraries(oaf_runtime PUBLIC ${LIBFFI_LIBRARIES})
endif()

add_executable(
    oaf_error_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/error/tests/error_smoke.c
)

target_link_libraries(oaf_error_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_memory_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/memory/tests/memory_smoke.c
)

target_link_libraries(oaf_memory_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_runtime_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/core/tests/runtime_smoke.c
)

target_link_libraries(oaf_runtime_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_types_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/types/tests/types_smoke.c
)

target_link_libraries(oaf_types_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_concurrency_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/concurrency/tests/concurrency_smoke.c
)

target_link_libraries(oaf_concurrency_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_ffi_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/Runtime/ffi/tests/ffi_smoke.c
)

target_link_libraries(oaf_ffi_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_collections_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/collections/tests/collections_smoke.c
)

target_link_libraries(oaf_collections_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_stdlib_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/tests/stdlib_smoke.c
)

target_link_libraries(oaf_stdlib_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_advanced_concurrency_smoke
    ${CMAKE_CURRENT_LIST_DIR}/../../src/stdlib/tests/advanced_concurrency_smoke.c
)

target_link_libraries(oaf_advanced_concurrency_smoke PRIVATE oaf_runtime)

add_executable(
    oaf_example_lightweight_threads
    ${CMAKE_CURRENT_LIST_DIR}/../../examples/runtime/01_lightweight_threads_and_channels.c
)

target_link_libraries(oaf_example_lightweight_threads PRIVATE oaf_runtime)

add_executable(
    oaf_example_task_parallel
    ${CMAKE_CURRENT_LIST_DIR}/../../examples/runtime/02_task_pool_async_and_parallel.c
)

target_link_libraries(oaf_example_task_parallel PRIVATE oaf_runtime)
