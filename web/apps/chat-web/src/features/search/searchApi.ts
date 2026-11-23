import type { SearchResponse } from '../../shared/api/contracts';
import { getJson } from '../../shared/api/httpClient';

/** Search filters (Phase 14); empty values are left out of the request. */
export interface SearchFilters {
  conversationId?: string;
  senderId?: string;
  /** yyyy-mm-dd in the user's time zone: from the start of that day. */
  fromDate?: string;
  /** yyyy-mm-dd in the user's time zone: up to the end of that day. */
  toDate?: string;
  hasFiles?: boolean;
}

export async function searchMessages(query: string, filters: SearchFilters, cursor?: string | null, signal?: AbortSignal): Promise<SearchResponse> {
  const params = new URLSearchParams({ q: query });
  if (filters.conversationId) params.set('conversationId', filters.conversationId);
  if (filters.senderId) params.set('senderId', filters.senderId);
  if (filters.fromDate) params.set('from', new Date(`${filters.fromDate}T00:00:00`).toISOString());
  if (filters.toDate) params.set('to', new Date(`${filters.toDate}T23:59:59.999`).toISOString());
  if (filters.hasFiles) params.set('hasFiles', 'true');
  if (cursor) params.set('cursor', cursor);
  return (await getJson<SearchResponse>(`/api/v1/search?${params.toString()}`, signal ? { signal } : {})).body ?? { items: [], nextCursor: null };
}

/** The same word rules as the server: lower case, accents removed. */
export function normalizeWord(word: string): string {
  return word.normalize('NFD').replace(/\p{M}/gu, '').toLowerCase();
}

/** The words of a query, as the server matches them (each one as a prefix). */
export function queryTerms(query: string): string[] {
  return [...new Set((query.match(/[\p{L}\p{N}]+/gu) ?? []).map(normalizeWord))];
}

/** Splits text into parts, marking the words that start with one of the terms. */
export function highlight(text: string, terms: string[]): { text: string; match: boolean }[] {
  if (terms.length === 0) return [{ text, match: false }];
  const parts: { text: string; match: boolean }[] = [];
  let last = 0;
  for (const word of text.matchAll(/[\p{L}\p{N}]+/gu)) {
    const index = word.index ?? 0;
    const normalized = normalizeWord(word[0]);
    if (terms.some((term) => normalized.startsWith(term))) {
      if (index > last) parts.push({ text: text.slice(last, index), match: false });
      parts.push({ text: word[0], match: true });
      last = index + word[0].length;
    }
  }
  if (last < text.length) parts.push({ text: text.slice(last), match: false });
  return parts;
}
