import { describe, it, expect, vi, beforeEach } from 'vitest'
import { useApiFetch, apiRequest } from './useApiFetch'
import * as apiModule from '../api'

// Mock the api module
vi.mock('../api', () => ({
    apiFetch: vi.fn(),
}))

describe('useApiFetch', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('returns initial state with null data and error', () => {
        const { data, error, isLoading } = useApiFetch()

        expect(data.value).toBeNull()
        expect(error.value).toBeNull()
        expect(isLoading.value).toBe(false)
    })

    it('sets loading state during request', async () => {
        const mockResponse = {
            ok: true,
            json: () => Promise.resolve({ id: 1 }),
        }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const { isLoading, execute } = useApiFetch()

        const promise = execute('/api/test')
        expect(isLoading.value).toBe(true)

        await promise
        expect(isLoading.value).toBe(false)
    })

    it('populates data on successful response', async () => {
        const mockData = { id: 1, name: 'test' }
        const mockResponse = {
            ok: true,
            json: () => Promise.resolve(mockData),
        }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const { data, execute } = useApiFetch<typeof mockData>()
        const result = await execute('/api/test')

        expect(result).toEqual(mockData)
        expect(data.value).toEqual(mockData)
    })

    it('populates error on failed response', async () => {
        const mockResponse = {
            ok: false,
            status: 500,
        }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const { data, error, execute } = useApiFetch()
        const result = await execute('/api/test')

        expect(result).toBeNull()
        expect(error.value).toBeInstanceOf(Error)
        expect(data.value).toBeNull()
    })

    it('resets state to initial values', async () => {
        const mockResponse = {
            ok: true,
            json: () => Promise.resolve({ id: 1 }),
        }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const { data, error, isLoading, execute, reset } = useApiFetch()
        await execute('/api/test')
        expect(data.value).not.toBeNull()

        reset()
        expect(data.value).toBeNull()
        expect(error.value).toBeNull()
        expect(isLoading.value).toBe(false)
    })
})

describe('apiRequest', () => {
    beforeEach(() => {
        vi.clearAllMocks()
    })

    it('returns parsed data on success', async () => {
        const mockData = { success: true }
        const mockResponse = {
            ok: true,
            json: () => Promise.resolve(mockData),
        }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const result = await apiRequest<typeof mockData>('/api/test')
        expect(result).toEqual(mockData)
    })

    it('returns null on failed response', async () => {
        const mockResponse = { ok: false, status: 404 }
        vi.mocked(apiModule.apiFetch).mockResolvedValue(mockResponse as unknown as Response)

        const result = await apiRequest('/api/test')
        expect(result).toBeNull()
    })

    it('returns null on network error', async () => {
        vi.mocked(apiModule.apiFetch).mockRejectedValue(new TypeError('Network error'))

        const result = await apiRequest('/api/test')
        expect(result).toBeNull()
    })
})
