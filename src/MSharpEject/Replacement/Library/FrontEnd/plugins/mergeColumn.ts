export default class MergeColumn {
    static mergeActionButtons() {
        $("table tr > .actions-merge, .r-grid .r-grid-row > .actions-merge").parent().each((_: number, row: any) => {
            const cells = $(row).children(".actions-merge");
            if (cells.length < 2) return;
            const first = cells.first();
            cells.not(first).each((__: number, c: any) => { first.append(" ").append($(c).contents()); $(c).remove(); });
        });
    }
}
