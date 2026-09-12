import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminService, AttributeSet, CatalogService, Category, ProblemDetails } from 'data-access';
import { firstValueFrom } from 'rxjs';

/** A category plus how deep it sits, so the tree can be rendered as a flat, indented list. */
interface Row {
  category: Category;
  depth: number;
}

@Component({
  selector: 'app-categories',
  imports: [FormsModule],
  templateUrl: './categories.html',
  styleUrl: './categories.scss',
})
export class Categories {
  private readonly admin = inject(AdminService);
  private readonly catalog = inject(CatalogService);

  protected readonly tree = signal<Category[]>([]);
  protected readonly attributeSets = signal<AttributeSet[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);
  protected readonly notice = signal<string | null>(null);

  protected readonly editingId = signal<string | null>(null);
  protected edit = { name: '', sortOrder: 0, isActive: true, attributeSetId: '' };

  protected readonly creating = signal(false);
  protected create = { name: '', parentId: '', sortOrder: 0, attributeSetId: '' };

  /** Flattened depth-first, which is how the list reads top to bottom. */
  protected readonly rows = computed(() => {
    const out: Row[] = [];
    const walk = (list: Category[], depth: number) => {
      for (const c of [...list].sort((a, b) => a.sortOrder - b.sortOrder)) {
        out.push({ category: c, depth });
        if (c.children?.length) walk(c.children, depth + 1);
      }
    };
    walk(this.tree(), 0);
    return out;
  });

  /** Only a leaf carries an attribute set, and only a parent can take children. */
  protected readonly parents = computed(() => this.rows().filter((r) => r.depth === 0));

  constructor() {
    void this.load();
  }

  protected indent(depth: number): string {
    return `${depth * 1.4}rem`;
  }

  protected setName(id: string): string {
    return this.attributeSets().find((s) => s.id === id)?.name ?? '—';
  }

  protected startEdit(c: Category): void {
    this.editingId.set(c.id);
    this.creating.set(false);
    this.edit = {
      name: c.name,
      sortOrder: c.sortOrder,
      isActive: c.isActive,
      attributeSetId: c.attributeSetId ?? '',
    };
  }

  protected startCreate(): void {
    this.creating.set(true);
    this.editingId.set(null);
    this.create = { name: '', parentId: '', sortOrder: this.parents().length, attributeSetId: '' };
  }

  protected async saveEdit(id: string): Promise<void> {
    if (this.busy()) return;
    this.busy.set(id);
    this.error.set(null);
    this.notice.set(null);

    try {
      await firstValueFrom(
        this.admin.updateCategory(id, {
          name: this.edit.name.trim(),
          sortOrder: Number(this.edit.sortOrder) || 0,
          isActive: this.edit.isActive,
          attributeSetId: this.edit.attributeSetId || null,
        }),
      );
      this.editingId.set(null);
      this.notice.set('Category saved.');
      await this.load();
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  protected async saveCreate(): Promise<void> {
    if (this.busy()) return;
    this.busy.set('new');
    this.error.set(null);
    this.notice.set(null);

    try {
      await firstValueFrom(
        this.admin.createCategory({
          name: this.create.name.trim(),
          parentId: this.create.parentId || null,
          sortOrder: Number(this.create.sortOrder) || 0,
          attributeSetId: this.create.attributeSetId || null,
        }),
      );
      this.creating.set(false);
      this.notice.set('Category added.');
      await this.load();
    } catch (err) {
      this.error.set(this.describe(err));
    } finally {
      this.busy.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    try {
      const [tree, sets] = await Promise.all([
        firstValueFrom(this.catalog.categories()),
        firstValueFrom(this.catalog.attributeSets()),
      ]);
      this.tree.set(tree);
      this.attributeSets.set(sets);
    } catch {
      this.error.set('Could not load categories.');
    } finally {
      this.loading.set(false);
    }
  }

  private describe(err: unknown): string {
    const problem = err instanceof HttpErrorResponse ? (err.error as ProblemDetails | null) : null;
    if (problem?.code === 'catalog.category_slug_taken') return 'A category with that name already exists here.';
    if (problem?.code === 'catalog.category_has_products') {
      return 'That category has products in it, so it cannot be deactivated yet.';
    }
    if (problem?.errors) return Object.values(problem.errors).flat().join(' ');
    return problem?.detail ?? 'That change could not be saved.';
  }
}
