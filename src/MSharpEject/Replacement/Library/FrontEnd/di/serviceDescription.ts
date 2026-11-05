import { ServiceContainer } from "./serviceContainer";

export class ServiceDescription {
    private factory: (...args: IService[]) => IService;
    private dependencies: string[] = [];
    private instance: IService;

    constructor(public key: string, private singleton: boolean, private container: ServiceContainer) { }

    setFactory(factory: (...args: IService[]) => IService): ServiceDescription {
        this.factory = factory;
        return this;
    }

    addDependency = (dep: string): ServiceDescription => {
        this.dependencies.push(dep);
        return this;
    }

    addDependencies = (...deps: string[]): ServiceDescription => {
        deps.forEach(this.addDependency);
        return this;
    }

    withDependencies(...deps: string[]): ServiceDescription {
        this.dependencies = [];
        return this.addDependencies(...deps);
    }

    getInstance = (): IService => {
        if (!this.singleton) return this.createInstance();
        return this.instance || (this.instance = this.createInstance());
    }

    private createInstance(): IService {
        return this.factory(...this.dependencies.map(d => this.container.getService(d)));
    }
}
