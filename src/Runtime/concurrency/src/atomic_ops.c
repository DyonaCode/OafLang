#include "atomic_ops.h"
#include <stddef.h>

void oaf_atomic_i64_init(OafAtomicI64* atomic_value, int64_t initial_value)
{
    if (atomic_value == NULL)
    {
        return;
    }

    atomic_init(&atomic_value->value, initial_value);
}

int64_t oaf_atomic_i64_load(const OafAtomicI64* atomic_value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_load(&atomic_value->value);
}

void oaf_atomic_i64_store(OafAtomicI64* atomic_value, int64_t value)
{
    if (atomic_value == NULL)
    {
        return;
    }

    atomic_store(&atomic_value->value, value);
}

int64_t oaf_atomic_i64_fetch_add(OafAtomicI64* atomic_value, int64_t value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_fetch_add(&atomic_value->value, value);
}

int64_t oaf_atomic_i64_fetch_sub(OafAtomicI64* atomic_value, int64_t value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_fetch_sub(&atomic_value->value, value);
}

int oaf_atomic_i64_compare_exchange(OafAtomicI64* atomic_value, int64_t* expected, int64_t desired)
{
    if (atomic_value == NULL || expected == NULL)
    {
        return 0;
    }

    return atomic_compare_exchange_strong(&atomic_value->value, expected, desired) ? 1 : 0;
}

void oaf_atomic_u64_init(OafAtomicU64* atomic_value, uint64_t initial_value)
{
    if (atomic_value == NULL)
    {
        return;
    }

    atomic_init(&atomic_value->value, initial_value);
}

uint64_t oaf_atomic_u64_load(const OafAtomicU64* atomic_value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_load(&atomic_value->value);
}

void oaf_atomic_u64_store(OafAtomicU64* atomic_value, uint64_t value)
{
    if (atomic_value == NULL)
    {
        return;
    }

    atomic_store(&atomic_value->value, value);
}

uint64_t oaf_atomic_u64_fetch_add(OafAtomicU64* atomic_value, uint64_t value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_fetch_add(&atomic_value->value, value);
}

uint64_t oaf_atomic_u64_fetch_sub(OafAtomicU64* atomic_value, uint64_t value)
{
    if (atomic_value == NULL)
    {
        return 0;
    }

    return atomic_fetch_sub(&atomic_value->value, value);
}

int oaf_atomic_u64_compare_exchange(OafAtomicU64* atomic_value, uint64_t* expected, uint64_t desired)
{
    if (atomic_value == NULL || expected == NULL)
    {
        return 0;
    }

    return atomic_compare_exchange_strong(&atomic_value->value, expected, desired) ? 1 : 0;
}
